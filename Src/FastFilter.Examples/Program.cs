namespace Genbox.FastFilter.Examples;

internal static class Program
{
    private static void Main()
    {
        int[] data = [1, 42, 30, 481, 58];
        BinaryFuse8Filter<int> fuse = new BinaryFuse8Filter<int>(data);

        Console.WriteLine("Does it contain 42? " + (fuse.Contains(42) ? "Yes" : "Maybe"));
        Console.WriteLine("Does it contain 50? " + (fuse.Contains(50) ? "Yes" : "Maybe"));
    }
}