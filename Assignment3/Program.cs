using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Assignment3
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello Web Service :-)");
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
            if (string.IsNullOrEmpty(url))
                return false;

            var parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length < 2)
                return false;

            // Check if last part is numeric (ID)
            if (parts.Length >= 3 && int.TryParse(parts[parts.Length - 1], out _))
            {
                HasId = true;
                Id = parts[parts.Length - 1];
                Path = "/" + string.Join("/", parts.Take(parts.Length - 1));
            }
            else
            {
                HasId = false;
                Id = null;
                Path = url;
            }

            return true;
        }
    }

    public class RequestValidator
    {
        private readonly string[] validMethods = { "create", "read", "update", "delete", "echo" };

        public Response ValidateRequest(Request request)
        {
            // Check method
            if (string.IsNullOrEmpty(request.Method))
            {
                return new Response { Status = "4 Bad Request missing method" };
            }

            if (!validMethods.Contains(request.Method.ToLower()))
            {
                return new Response { Status = "4 Bad Request illegal method" };
            }

            // Check path
            if (string.IsNullOrEmpty(request.Path))
            {
                return new Response { Status = "4 Bad Request missing path" };
            }

            // Check date
            if (string.IsNullOrEmpty(request.Date))
            {
                return new Response { Status = "4 Bad Request missing date" };
            }

            // Validate date format (should be Unix timestamp)
            if (!long.TryParse(request.Date, out _))
            {
                return new Response { Status = "4 Bad Request illegal date" };
            }

            // Check body for methods that require it
            var methodsRequiringBody = new[] { "create", "update", "echo" };
            if (methodsRequiringBody.Contains(request.Method.ToLower()))
            {
                if (string.IsNullOrEmpty(request.Body))
                {
                    return new Response { Status = "4 Bad Request missing body" };
                }

                // For create and update, body should be valid JSON
                if (request.Method.ToLower() == "create" || request.Method.ToLower() == "update")
                {
                    try
                    {
                        JsonDocument.Parse(request.Body);
                    }
                    catch
                    {
                        return new Response { Status = "4 Bad Request illegal body" };
                    }
                }
            }

            return new Response { Status = "1 Ok" };
        }
    }

    public class CategoryService
    {
        private List<Category> categories;

        public CategoryService()
        {
            categories = new List<Category>
            {
                new Category { Id = 1, Name = "Beverages" },
                new Category { Id = 2, Name = "Condiments" },
                new Category { Id = 3, Name = "Dairy Products" }
            };
        }

        public List<Category> GetCategories()
        {
            return categories.ToList();
        }

        public Category GetCategory(int id)
        {
            return categories.FirstOrDefault(c => c.Id == id);
        }

        public bool UpdateCategory(int id, string name)
        {
            var category = categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
                return false;

            category.Name = name;
            return true;
        }

        public bool DeleteCategory(int id)
        {
            var category = categories.FirstOrDefault(c => c.Id == id);
            if (category == null)
                return false;

            categories.Remove(category);
            return true;
        }

        public bool CreateCategory(int id, string name)
        {
            if (categories.Any(c => c.Id == id))
                return false;

            categories.Add(new Category { Id = id, Name = name });
            return true;
        }
    }
}
