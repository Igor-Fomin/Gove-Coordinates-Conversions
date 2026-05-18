using System;
using netDxf;
using netDxf.Entities;
using System.Reflection;

namespace Inspector
{
    class Program
    {
        static void Main()
        {
            var entities = new DxfDocument().Entities;
            var type = entities.GetType();
            Console.WriteLine($"Type: {type.FullName}");
            foreach (var prop in type.GetProperties())
            {
                Console.WriteLine($"Property: {prop.Name}");
            }
        }
    }
}
