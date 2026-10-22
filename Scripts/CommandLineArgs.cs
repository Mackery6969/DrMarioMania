using Godot;
using System;
using System.Linq;

// Reads "--name=value" and "--name" arguments given at launch
// User args are the ones after "--" (e.g. when running through the Godot executable), the rest are passed straight to exported builds
public static class CommandLineArgs
{
    private static string[] args;
    private static string[] Args
    {
        get
        {
            if (args == null)
                args = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs()).ToArray();

            return args;
        }
    }

    public static bool Has(string name)
    {
        return Args.Contains("--" + name) || Get(name) != null;
    }

    // Returns the value of "--name=value", or null if it wasn't given
    public static string Get(string name)
    {
        string prefix = "--" + name + "=";

        foreach (string arg in Args)
        {
            if (arg.StartsWith(prefix))
                return arg.Substring(prefix.Length);
        }

        return null;
    }

    public static int GetInt(string name, int fallback)
    {
        return int.TryParse(Get(name), out int value) ? value : fallback;
    }

    public static float GetFloat(string name, float fallback)
    {
        return float.TryParse(Get(name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : fallback;
    }
}
