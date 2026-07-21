# Welcome to the jask interpreter repository!
> [!NOTE]
> The interpreter is fully written in C# without other dependencies.
> You only have to install [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) in order to develop for jask.

# Executing
Navigate to the root of the repository and
```terminal
dotnet run
```
This will build and execute the interpreter and jumps directly into the interactive mode.
Per default, the interpreter lacks all permissions for in- and output as well as reading and writing files.
One has for example pass the permission *allow-stdout* on startup, so that jask can use the *print* function:
```python
dotnet run --allow-stdout
>>> print("Hello World!") 
Hello World!
```
Please note that the interpreter has always the permission to write runtime errors to *stderr*.
## Permissions
### allow-stdout
Allows jask to use *print*
### allow-stdin
Allows jask to use *readInput*
### allow-read and allow-write
Can be used with or without paths and multiple times to define exactly the paths and files jask is allowed to read or write.
Please note, that the interpreter inherits the permissions of the executing user regarding file access.
```terminal
dotnet run --allow-read
dotnet run --allow-read="sample.jask"
dotnet run --allow-read="a/path/to/a/directory --allow-write="a_single_file.txt"
```
Using *allow-read* and *allow-write* without paths enables permissions globally.
### allow-trust
Allows the usage of `trust()` for external provided, untrusted values.
Should not be used, since simply trusting external input can be dangerous.
Untrusted values should be verified using `verify()` instead.
### allow-all
Combines all permissions in one flag.
Can be dangerous, so should only be used in testing.
## jask jcore
Part of the jask language is *jcore*, a library fully written in jask.
Check out the repository [here](https://github.com/jask-lang/jcore).
