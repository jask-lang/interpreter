namespace JaskLang;

[Flags]
public enum Permission : uint
{
    None      = 0,
    Stdout    = 1 << 0,
    Stdin     = 1 << 1,
    FileRead  = 1 << 2,
    FileWrite = 1 << 3,
    Network   = 1 << 4,
    Trust     = 1 << 5,
    All = Stdout | Stdin | FileRead | FileWrite | Network | Trust
}

public class ArgumentsParser
{
    private readonly Dictionary<string, List<string>> _parameters = new(StringComparer.OrdinalIgnoreCase);

    public ArgumentsParser(IEnumerable<string> args)
    {
        var arguments = args.ToArray();
        for (int i = 0; i < arguments.Length; i++)
        {
            string arg = arguments[i];

            // jask supports '--flag=value' and '--flag value'
            int separatorIdx = arg.IndexOfAny(['=']);

            // flag without value in one place (e.g --allow-stdout or --input example.jask)
            if (separatorIdx == -1)
            {
                if (arg.Equals("--input") || arg.Equals("-i") ||
                    arg.Equals("--allow-read") || arg.Equals("-ar") ||
                    arg.Equals("--allow-write") || arg.Equals("-aw"))
                {
                    string value = arguments[++i];

                    if (value.StartsWith("-") == true)
                    {
                        // the value is just another arg, so we are skipping
                        _parameters[arg] = [];
                        --i;
                        continue;
                    }

                    if (_parameters.TryGetValue(arg, out var list) == false)
                    {
                        list = [];
                        
                        _parameters[arg] = list;
                    }
                    list.Add(value);
                }
                else if (_parameters.ContainsKey(arg) == false)
                {
                    _parameters[arg] = [];
                }
            }
            else
            {
                // flag with value in one place (e.g --allow-read="/a/sample/path/")
                string key = arg[..separatorIdx];
                string value = arg[(separatorIdx + 1)..];

                if (_parameters.TryGetValue(key, out var list) == false)
                {
                    list = [];
                    
                    _parameters[key] = list;
                }
                list.Add(value);
            }
        }
    }

    // checks, if a flag is set
    public bool Has(string flag) => _parameters.ContainsKey(flag);

    // gets all values for a flag
    public IEnumerable<string> GetValues(string flag) => 
        _parameters.TryGetValue(flag, out var list) ? list : Enumerable.Empty<string>();
}

public class PermissionManager
{
    private Permission _permissions = Permission.None;
    private readonly List<string> _allowedReadPaths = new();
    private readonly List<string> _allowedWritePaths = new();

    public PermissionManager() { }

    public PermissionManager(ArgumentsParser argumentParser)
    {
        // parse allow all flag
        if (argumentParser.Has("--allow-all"))
        {
            Grant(Permission.All);
            return;
        }

        // parse simple flags
        if (argumentParser.Has("--allow-stdout") || argumentParser.Has("-ao"))
        {
            Grant(Permission.Stdout);
        }
        if (argumentParser.Has("--allow-stdin") || argumentParser.Has("-ai"))
        {
            Grant(Permission.Stdin);
        }
        if (argumentParser.Has("--allow-network") || argumentParser.Has("-an"))
        {
            Grant(Permission.Network);
        }
        if (argumentParser.Has("--allow-trust") || argumentParser.Has("-at"))
        {
            Grant(Permission.Trust);
        }

        // parse multiple flags for file read
        if (argumentParser.Has("--allow-read") || argumentParser.Has("-ar"))
        {
            Grant(Permission.FileRead);
            foreach (var path in argumentParser.GetValues("--allow-read"))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AddNormalizedPath(_allowedReadPaths, path);
                }
            }
            foreach (var path in argumentParser.GetValues("-ar"))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AddNormalizedPath(_allowedReadPaths, path);
                }
            }
        }

        // parse multiple flags for file write
        if (argumentParser.Has("--allow-write") || argumentParser.Has("-aw"))
        {
            Grant(Permission.FileWrite);
            foreach (var path in argumentParser.GetValues("--allow-write"))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AddNormalizedPath(_allowedWritePaths, path);
                }
            }
            foreach (var path in argumentParser.GetValues("-aw"))
            {
                if (!string.IsNullOrWhiteSpace(path))
                {
                    AddNormalizedPath(_allowedWritePaths, path);
                }
            }
        }
    }

    /// <summary>
    /// Helper for normalizing paths, prevents path traversals ("../../")
    /// </summary>
    private void AddNormalizedPath(List<string> pathList, string rawPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(rawPath);
            if (!pathList.Contains(fullPath))
            {
                pathList.Add(fullPath);
            }
        }
        catch
        {
            // ignore invalid paths
        }
    }

    /// <summary>
    /// Grant one or more rights
    /// </summary>
    public void Grant(Permission permission)
    {
        _permissions |= permission;
    }

    public bool IsPermitted(Permission permission)
    {
        return (_permissions & permission) == permission;
    }

    public bool IsPathPermitted(Permission direction, string targetPath)
    {
        if (IsPermitted(direction) == false)
        {
            return false;
        }

        var allowedList = direction == Permission.FileRead ? _allowedReadPaths : _allowedWritePaths;

        // flag without a path, allow implicitly everything (*)
        if (allowedList.Count == 0)
        {
            return true;
        }

        try
        {
            // resolve relative parts and standardize slashes for the target
            string fullTargetPath = Path.GetFullPath(targetPath);

            // select appropriate comparison for operating system
            StringComparison comparison = OperatingSystem.IsWindows() 
                ? StringComparison.OrdinalIgnoreCase 
                : StringComparison.Ordinal;

            foreach (var rawAllowed in allowedList)
            {
                if (string.IsNullOrWhiteSpace(rawAllowed))
                {
                    continue;
                }

                // normalize the allowed path
                string fullAllowedPath = Path.GetFullPath(rawAllowed);

                // exact match (file or directory match)
                if (fullTargetPath.Equals(fullAllowedPath, comparison))
                {
                    return true;
                }

                // ensure that allowed path ends with a separator before checking directory prefix
                // this prevents false positives like matching "C:\App" against "C:\AppSecret"
                string prefix = fullAllowedPath.EndsWith(Path.DirectorySeparatorChar) || fullAllowedPath.EndsWith(Path.AltDirectorySeparatorChar)
                    ? fullAllowedPath
                    : fullAllowedPath + Path.DirectorySeparatorChar;

                if (fullTargetPath.StartsWith(prefix, comparison))
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }
}