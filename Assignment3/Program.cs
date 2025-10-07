using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Assignment3
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello Web Service :-)");
            int port = 5001;
            var server = new EchoServer(port);
            server.Run();
        }
    }

    public class Request
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public string Date { get; set; }
        public string Body { get; set; }
    }

    public class Response
    {
        public string Status { get; set; }
        public string Body { get; set; }
    }

    public class Category
    {
        [JsonPropertyName("cid")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; }
    }

    public class UrlParser
    {
        public string Path { get; private set; }
        public string Id { get; private set; }
        public bool HasId { get; private set; }

        public bool ParseUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length > 1 && int.TryParse(parts.Last(), out _))
            {
                HasId = true;
                Id = parts.Last();
                Path = "/" + string.Join('/', parts.Take(parts.Length - 1));
            }
            else
            {
                HasId = false;
                Path = "/" + string.Join('/', parts);
            }

            return true;
        }
    }

    public class RequestValidator
    {
        private readonly HashSet<string> _validMethods =
            new() { "create", "read", "update", "delete", "echo" };

        public Response ValidateRequest(Request request)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.Method))
                errors.Add("missing method");
            else if (!_validMethods.Contains(request.Method.ToLower()))
                errors.Add("illegal method");

            if (string.IsNullOrWhiteSpace(request.Path))
                errors.Add("missing path");

            if (string.IsNullOrWhiteSpace(request.Date))
                errors.Add("missing date");
            else if (!long.TryParse(request.Date, out _))
                errors.Add("illegal date");

            if (!string.IsNullOrWhiteSpace(request.Method))
            {
                var method = request.Method.ToLower();
                if (method is "create" or "update")
                {
                    if (string.IsNullOrWhiteSpace(request.Body))
                        errors.Add("missing body");
                    else
                    {
                        try { JsonDocument.Parse(request.Body); }
                        catch { errors.Add("illegal body"); }
                    }
                }
                else if (method == "echo" && string.IsNullOrWhiteSpace(request.Body))
                {
                    errors.Add("missing body");
                }
            }

            return new Response
            {
                Status = errors.Any() ? "4 " + string.Join(", ", errors) : "1 Ok"
            };
        }
    }

    public class CategoryService
    {
        private readonly List<Category> _categories;

        public CategoryService()
        {
            _categories = new List<Category>
            {
                new() { Id = 1, Name = "Beverages" },
                new() { Id = 2, Name = "Condiments" },
                new() { Id = 3, Name = "Confections" }
            };
        }

        public List<Category> GetCategories() => _categories.ToList();
        public Category GetCategory(int id) => _categories.FirstOrDefault(c => c.Id == id);

        public bool UpdateCategory(int id, string newName)
        {
            var category = GetCategory(id);
            if (category == null) return false;
            category.Name = newName;
            return true;
        }

        public bool DeleteCategory(int id)
        {
            var category = GetCategory(id);
            if (category == null) return false;
            _categories.Remove(category);
            return true;
        }

        // Part I overload (used by Part I tests)
        public bool CreateCategory(int id, string name)
        {
            if (_categories.Any(c => c.Id == id)) return false;
            _categories.Add(new Category { Id = id, Name = name });
            return true;
        }

        // Part II overload (auto-id for API create)
        public Category CreateCategory(string name)
        {
            int newId = _categories.Any() ? _categories.Max(c => c.Id) + 1 : 1;
            var newCategory = new Category { Id = newId, Name = name };
            _categories.Add(newCategory);
            return newCategory;
        }
    }

    public class EchoServer
    {
        private TcpListener _server;
        public int Port { get; set; }
        private readonly CategoryService _categoryService = new();
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public EchoServer(int port) { Port = port; }

        public void Run()
        {
            _server = new TcpListener(IPAddress.Loopback, Port);
            _server.Start();
            Console.WriteLine($"Server started on port {Port}");

            // Blocking accept loop - keep the process alive so external test runners
            // can connect to the service at the expected port.
            while (true)
            {
                try
                {
                    var client = _server.AcceptTcpClient(); // blocks until a client connects
                    _ = Task.Run(() => HandleClient(client));
                }
                catch (SocketException)
                {
                    // Listener stopped or error occurred - exit loop
                    break;
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (client)
            using (var strm = client.GetStream())
            {
                strm.ReadTimeout = 2000;

                string requestText = ReadRequestText(strm);

                // If there is nothing to read (empty or whitespace), just ignore and leave the socket open a bit
                if (string.IsNullOrWhiteSpace(requestText))
                {
                    // No response (first test simply checks you can connect)
                    return;
                }

                Response response;

                // Try to parse the request JSON
                Request request = null;
                try
                {
                    request = JsonSerializer.Deserialize<Request>(requestText, _jsonOptions);
                }
                catch
                {
                    // If the JSON is completely malformed, treat it as bad request
                    response = new Response { Status = "4 Bad Request" };
                    WriteResponseAndClose(strm, response);
                    return;
                }

                // Validate basic CJTP fields
                var validator = new RequestValidator();
                var validation = validator.ValidateRequest(request);
                if (!validation.Status.StartsWith("1"))
                {
                    WriteResponseAndClose(strm, validation);
                    return;
                }

                // If valid, route and execute
                response = RouteAndExecute(request);

                WriteResponseAndClose(strm, response);
            }
        }

        private string ReadRequestText(NetworkStream strm)
        {
            var buf = new byte[2048];
            using var ms = new MemoryStream();
            try
            {
                while (true)
                {
                    int n = strm.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    ms.Write(buf, 0, n);

                    // If we read less than the buffer or no more data is available, assume request is complete
                    if (n < buf.Length && !strm.DataAvailable) break;
                }
            }
            catch (IOException)
            {
                // Read timed out — treat as no/partial request
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private void WriteResponseAndClose(NetworkStream strm, Response response)
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, _jsonOptions));
            strm.Write(bytes, 0, bytes.Length);
            // Important for tests: close after single request/response
            strm.Flush();
            try { strm.Dispose(); } catch { /* ignore */ }
        }

        private Response RouteAndExecute(Request req)
        {
            var method = req.Method.ToLowerInvariant();
            var path = req.Path ?? string.Empty;

            // Only API supported: /api/categories
            bool isCategoriesRoot = string.Equals(path, "/api/categories", StringComparison.OrdinalIgnoreCase);
            bool isCategoriesWithTail = path.StartsWith("/api/categories/", StringComparison.OrdinalIgnoreCase);

            if (!(isCategoriesRoot || isCategoriesWithTail))
            {
                return new Response { Status = "5 Not found" }; // Unknown path
            }

            // If there's a tail after /api/categories/, ensure it's a valid integer id; otherwise it's a Bad Request
            int id = -1;
            if (isCategoriesWithTail)
            {
                var tail = path.Substring("/api/categories/".Length);
                if (!int.TryParse(tail, out id))
                {
                    return new Response { Status = "4 Bad Request" };
                }
            }

            switch (method)
            {
                case "read":
                    if (isCategoriesRoot)
                    {
                        var all = _categoryService.GetCategories();
                        var body = JsonSerializer.Serialize(all, _jsonOptions);
                        return new Response { Status = "1 Ok", Body = body };
                    }
                    else
                    {
                        var cat = _categoryService.GetCategory(id);
                        if (cat == null) return new Response { Status = "5 Not found" };
                        var body = JsonSerializer.Serialize(cat, _jsonOptions);
                        return new Response { Status = "1 Ok", Body = body };
                    }

                case "create":
                    if (!isCategoriesRoot)
                    {
                        // create must not have an id in the path
                        return new Response { Status = "4 Bad Request" };
                    }
                    try
                    {
                        using var doc = JsonDocument.Parse(req.Body);
                        string name = doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                                      ? nameProp.GetString()
                                      : null;
                        if (string.IsNullOrWhiteSpace(name))
                            return new Response { Status = "4 Bad Request" };

                        var created = _categoryService.CreateCategory(name);
                        var body = JsonSerializer.Serialize(created, _jsonOptions);
                        return new Response { Status = "2 Created", Body = body };
                    }
                    catch
                    {
                        return new Response { Status = "4 Bad Request" };
                    }

                case "update":
                    if (!isCategoriesWithTail)
                    {
                        // update must include id
                        return new Response { Status = "4 Bad Request" };
                    }
                    try
                    {
                        using var doc = JsonDocument.Parse(req.Body);
                        string name = doc.RootElement.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                                      ? nameProp.GetString()
                                      : null;
                        if (string.IsNullOrWhiteSpace(name))
                            return new Response { Status = "4 Bad Request" };

                        var ok = _categoryService.UpdateCategory(id, name);
                        if (!ok) return new Response { Status = "5 Not found" };
                        return new Response { Status = "3 Updated" };
                    }
                    catch
                    {
                        return new Response { Status = "4 Bad Request" };
                    }

                case "delete":
                    if (!isCategoriesWithTail)
                    {
                        // delete must include id
                        return new Response { Status = "4 Bad Request" };
                    }
                    var deleted = _categoryService.DeleteCategory(id);
                    if (!deleted) return new Response { Status = "5 Not found" };
                    return new Response { Status = "1 Ok" };

                default:
                    // Shouldn't happen; validator already guards this
                    return new Response { Status = "4 Illegal method" };
            }
        }
    }
}
