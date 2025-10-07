using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Assignment3
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello Web Service :-)");
            var server = new EchoServer(5001);
            server.Run();
        }
    }

    // Data Models

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

    // Url Parser
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

    // Request validation

    public class RequestValidator
    {
        private readonly HashSet<string> validMethods = new() { "create", "read", "update", "delete", "echo" };

        public Response ValidateRequest(Request r)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(r.Method)) errors.Add("missing method");
            else if (!validMethods.Contains(r.Method.ToLower())) errors.Add("illegal method");

            if (string.IsNullOrWhiteSpace(r.Path)) errors.Add("missing path");

            if (string.IsNullOrWhiteSpace(r.Date)) errors.Add("missing date");
            else if (!long.TryParse(r.Date, out _)) errors.Add("illegal date");

            if (r.Method?.ToLower() is "create" or "update" or "echo")
            {
                if (string.IsNullOrWhiteSpace(r.Body)) errors.Add("missing body");
                else
                {
                    if (r.Method.ToLower() != "echo")
                    {
                        try { JsonDocument.Parse(r.Body); }
                        catch { errors.Add("illegal body"); }
                    }
                }
            }

            return new Response
            {
                Status = errors.Count > 0 ? "4 " + string.Join(", ", errors) : "1 Ok"
            };
        }
    }

    // Category Service 

    public class CategoryService
    {
        private readonly List<Category> categories = new()
        {
            new() { Id = 1, Name = "Beverages" },
            new() { Id = 2, Name = "Condiments" },
            new() { Id = 3, Name = "Confections" }
        };

        public List<Category> GetCategories() => categories.ToList();
        public Category GetCategory(int id) => categories.FirstOrDefault(c => c.Id == id);

        public bool UpdateCategory(int id, string newName)
        {
            var c = GetCategory(id);
            if (c == null) return false;
            c.Name = newName;
            return true;
        }

        public bool DeleteCategory(int id)
        {
            var c = GetCategory(id);
            if (c == null) return false;
            categories.Remove(c);
            return true;
        }
        public bool CreateCategory(int id, string name)
        {
            if (categories.Any(c => c.Id == id)) return false;
            categories.Add(new Category { Id = id, Name = name });
            return true;
        }

        public Category CreateCategory(string name)
        {
            int newId = categories.Any() ? categories.Max(c => c.Id) + 1 : 1;
            var c = new Category { Id = newId, Name = name };
            categories.Add(c);
            return c;
        }
    }

    // Echo Server

    public class EchoServer
    {
        private readonly int port;
        private TcpListener listener;
        private readonly CategoryService service = new();
        private readonly JsonSerializerOptions json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public EchoServer(int port) { this.port = port; }

        public void Run()
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            Console.WriteLine($"Server running on port {port}");

            while (true)
            {
                var client = listener.AcceptTcpClient();
                Task.Run(() => HandleClient(client));
            }
        }

        private void HandleClient(TcpClient client)
        {
            using var stream = client.GetStream();
            string text = ReadRequest(stream);
            if (string.IsNullOrWhiteSpace(text)) return;

            Request req;
            try { req = JsonSerializer.Deserialize<Request>(text, json); }
            catch { Write(stream, new Response { Status = "4 Bad Request" }); return; }

            var validator = new RequestValidator();
            var check = validator.ValidateRequest(req);
            if (!check.Status.StartsWith("1")) { Write(stream, check); return; }

            Write(stream, Route(req));
        }

        private string ReadRequest(NetworkStream s)
        {
            var buf = new byte[2048];
            int n = s.Read(buf, 0, buf.Length);
            return Encoding.UTF8.GetString(buf, 0, n);
        }

        private void Write(NetworkStream s, Response r)
        {
            var data = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r, json));
            s.Write(data, 0, data.Length);
        }

        // Routing
        private Response Route(Request req)
        {
            string method = req.Method.ToLower();
            var parser = new UrlParser();
            if (!parser.ParseUrl(req.Path)) return Bad();

            if (!parser.Path.Equals("/api/categories", StringComparison.OrdinalIgnoreCase))
                return NotFound();

            int id = parser.HasId ? int.Parse(parser.Id) : -1;

            return method switch
            {
                "read"   => !parser.HasId ? Ok(service.GetCategories()) : (service.GetCategory(id) is { } c ? Ok(c) : NotFound()),
                "create" => parser.HasId ? Bad() : Create(req.Body),
                "update" => parser.HasId ? Update(id, req.Body) : Bad(),
                "delete" => parser.HasId ? Delete(id) : Bad(),
                "echo"   => new Response { Status = "1 Ok", Body = req.Body },
                _        => Bad()
            };
        }

        private Response Ok(object data) =>
            new() { Status = "1 Ok", Body = JsonSerializer.Serialize(data, json) };

        private Response Create(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                string name = doc.RootElement.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name)) return Bad();
                var c = service.CreateCategory(name);
                return new Response { Status = "2 Created", Body = JsonSerializer.Serialize(c, json) };
            }
            catch { return Bad(); }
        }

        private Response Update(int id, string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                string name = doc.RootElement.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name)) return Bad();
                return service.UpdateCategory(id, name) ? new Response { Status = "3 Updated" } : NotFound();
            }
            catch { return Bad(); }
        }

        private Response Delete(int id) =>
            service.DeleteCategory(id) ? new Response { Status = "1 Ok" } : NotFound();

        private Response Bad() => new() { Status = "4 Bad Request" };
        private Response NotFound() => new() { Status = "5 Not found" };
    }
}
