// convert: <cache dir> <package> [<package> ...] -> the player's copy converted as the game does it (OriginalConverter).
using System; using System.Linq; using GoF2Remake.Data;
static class Program
{
    static int Main(string[] a)
    {
        if (a.Length < 2) { Console.Error.WriteLine("usage: convert <cache dir> <package (.ipa / .obb / .xapk / folder)> [...]"); return 2; }
        var t0 = DateTime.Now;
        var c = new OriginalConverter(a[0]);
        c.Run(a.Skip(1));
        foreach (var g in c.Items.GroupBy(i => i.Path.Split('/')[0])) Console.WriteLine($"{g.Key}: {g.Count()}");
        foreach (var p in c.Problems) Console.WriteLine("problem: " + p);
        Console.WriteLine($"{c.Items.Count} files, {c.Problems.Count} problems, {(DateTime.Now - t0).TotalSeconds:0.0} s");
        return 0;
    }
}
