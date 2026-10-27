using BetterTaskManager.Core.Tests;

// Console test runner: prints every test as file::name, its scenario and PASS or FAIL. The exit code is the number of
// failures.
var suites = new (string File, IEnumerable<(string Name, string Scenario, Action Body)> Tests)[]
{
    ("KernelPoolAlertTests.cs", KernelPoolAlertTests.All()),
    ("StoreTests.cs", StoreTests.All()),
    ("SafetyTests.cs", SafetyTests.All()),
};

int failures = 0, count = 0;
foreach (var (file, tests) in suites)
{
    foreach (var (name, scenario, body) in tests)
    {
        count++;
        string result;
        try
        {
            body();
            result = "PASS";
        }
        catch (Exception ex)
        {
            failures++;
            result = "FAIL: " + ex.Message;
        }
        Console.WriteLine($"{file}::{name} | {scenario} | {result}");
    }
}
Console.WriteLine(failures == 0 ? "All tests passed." : $"{failures} of {count} tests failed.");
return failures;

namespace BetterTaskManager.Core.Tests
{
    internal static class Check
    {
        public static void That(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"expected {expected}, got {actual}");
        }
    }
}
