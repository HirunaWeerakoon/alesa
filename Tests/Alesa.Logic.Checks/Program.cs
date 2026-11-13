using System;

namespace Alesa.Logic.Checks
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try
            {
                GraphAndAStarChecks.RunAll();
                return 0; // Success exit code
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Error.WriteLine($"\nTEST EXECUTION FAILED WITH EXCEPTION:\n{ex}");
                Console.ResetColor();
                return 1; // Error exit code
            }
        }
    }
}

