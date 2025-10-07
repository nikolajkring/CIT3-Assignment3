using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.IO;

namespace Assignment3
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello Web Service :-)");
            int port = 5000;
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

        // Part I overload
        public bool CreateCategory(int id, string name)
        {
            if (_categories.Any(c => c.Id == id)) return false;
            _categories.Add(new Category { Id = id, Name = name });
            return true;
        }

        // Part II overload
        public Category CreateCategory(string name)
        {
            int newId = _categories.Max(c => c.Id) + 1;
            var newCategory = new Category { Id = newId, Name = name };
            _categories.Add(newCategory);
            return newCategory;
        }
    }

    public class EchoServer
    {
        TcpListener _server;
        public int Port { get; set; }
        private readonly CategoryService _categoryService = new();

        public EchoServer(int port) { Port = port; }

        public void Run()
        {
            _server = new TcpListener(IPAddress.Loopback, Port);
            _server.Start();
            Console.WriteLine($"Server started on port {Port}");

            while (true)
            {
                TcpClient client = _server.AcceptTcpClient();
                _ = System.Threading.Tasks.Task.Run(() => HandleClient(client));
            }
        }

        private string ReadRequestJson(NetworkStream strm)
        {
            strm.ReadTimeout = 250;
            byte[] buffer = new byte[2048];
            using var ms = new MemoryStream();
            try
            {
                int bytesRead;
                do
                {
                    bytesRead = strm.Read(buffer, 0, buffer.Length);
                    if (bytesRead <= 0) break;
                    ms.Write(buffer, 0, bytesRead);
                    if (bytesRead < buffer.Length) break;
                    if (!strm.DataAvailable) break;
                } while (true);
            }
            catch (IOException) { }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        private void HandleClient(TcpClient client)
        {
            using var stream = client.GetStream();
            using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            try
            {
                string requestJson = ReadRequestJson(stream);
                if (string.IsNullOrWhiteSpace(requestJson))
                    return;

                var request = JsonSerializer.Deserialize<Request>(requestJson,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                var validator = new RequestValidator();
                var validationResponse = validator.ValidateRequest(request);
                if (!validationResponse.Status.StartsWith("1"))
                {
                    writer.Write(JsonSerializer.Serialize(validationResponse,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                    return;
                }

                var response = ProcessRequest(request);
                writer.Write(JsonSerializer.Serialize(response,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            catch
            {
                var errorResp = new Response { Status = "6 Error" };
                writer.Write(JsonSerializer.Serialize(errorResp,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
            finally
            {
                client.Close();
            }
        }

        private Response ProcessRequest(Request request)
        {
            var urlParser = new UrlParser();
            if (!urlParser.ParseUrl(request.Path))
                return new Response { Status = "4 Bad Request" };

            string method = request.Method.ToLower();

            switch (method)
            {
                case "echo":
                    return new Response { Status = "1 ok", Body = request.Body };

                case "read":
                    if (urlParser.Path == "/api/categories")
                    {
                        if (urlParser.HasId)
                        {
                            if (int.TryParse(urlParser.Id, out int id))
                            {
                                var cat = _categoryService.GetCategory(id);
                                if (cat == null)
                                    return new Response { Status = "5 Not found" };
                                return new Response
                                {
                                    Status = "1 Ok",
                                    Body = JsonSerializer.Serialize(cat,
                                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                                };
                            }
                            return new Response { Status = "4 Bad Request" };
                        }
                        else
                        {
                            var cats = _categoryService.GetCategories();
                            return new Response
                            {
                                Status = "1 Ok",
                                Body = JsonSerializer.Serialize(cats,
                                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                            };
                        }
                    }
                    break;

                case "create":
                    if (urlParser.Path == "/api/categories" && !urlParser.HasId)
                    {
                        try
                        {
                            var bodyDoc = JsonDocument.Parse(request.Body);
                            if (!bodyDoc.RootElement.TryGetProperty("name", out var nameEl))
                                return new Response { Status = "4 Bad Request" };

                            var newCat = _categoryService.CreateCategory(nameEl.GetString());
                            return new Response
                            {
                                Status = "2 Created",
                                Body = JsonSerializer.Serialize(newCat,
                                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                            };
                        }
                        catch
                        {
                            return new Response { Status = "4 illegal body" };
                        }
                    }
                    else return new Response { Status = "4 Bad Request" };

                case "update":
                    if (urlParser.Path == "/api/categories" && urlParser.HasId)
                    {
                        if (int.TryParse(urlParser.Id, out int id))
                        {
                            try
                            {
                                var cat = JsonSerializer.Deserialize<Category>(request.Body,
                                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                                if (_categoryService.UpdateCategory(id, cat.Name))
                                    return new Response { Status = "3 updated" };
                                else
                                    return new Response { Status = "5 Not found" };
                            }
                            catch
                            {
                                return new Response { Status = "4 illegal body" };
                            }
                        }
                    }
                    else return new Response { Status = "4 Bad Request" };
                    break;

                case "delete":
                    if (urlParser.Path == "/api/categories" && urlParser.HasId)
                    {
                        if (int.TryParse(urlParser.Id, out int id))
                        {
                            if (_categoryService.DeleteCategory(id))
                                return new Response { Status = "1 ok" };
                            else
                                return new Response { Status = "5 Not found" };
                        }
                    }
                    else return new Response { Status = "4 Bad Request" };
                    break;
            }

            return new Response { Status = "5 Not found" };
        }
    }
}
