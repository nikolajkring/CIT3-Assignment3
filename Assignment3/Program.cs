using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        public int Id { get; set; }
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

        public Category GetCategory(int id) =>
            _categories.FirstOrDefault(c => c.Id == id);

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

        public bool CreateCategory(int id, string name)
        {
            if (_categories.Any(c => c.Id == id)) return false;
            _categories.Add(new Category { Id = id, Name = name });
            return true;
        }
    }

    // Part2test
    public class EchoServer
    {

        TcpListener _server;

        public int Port { get; set; }

        public EchoServer(int port)
        {
            Port = port;
        }

        public void Run()
        {
            _server = new TcpListener(IPAddress.Loopback, Port);

            _server.Start();
            Console.WriteLine($"Server started on port {Port}");

            while (true)
            {
                TcpClient client = _server.AcceptTcpClient();
                Console.WriteLine("Client connected");
                HandleClient(client);
            }
        }

         private void HandleClient(TcpClient client)
        {
            var stream = client.GetStream();

            var msg = "Hello form server";

            stream.Write(Encoding.UTF8.GetBytes(msg));

        }


    }
}



