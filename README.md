# Welcome to the jask interpreter repository!
> [!NOTE]
> The interpreter is fully written in C# without other .NET dependencies.
> You need to have the [.NET 10.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) installed in order to develop for jask.
> The interpreter depends on [jcore](https://github.com/jask-lang/jcore), which needs to be placed directly next to this repository.

# Executing
Per default, the interpreter lacks permissions.
One has to pass the permission *allow-stdout* so that jask can use the *print* function:
```python
dotnet run --allow-stdout
>>> print("Hello World!") 
Hello World!
```
If the interpreter and jcore are set up, check out [the tutorials](https://github.com/jask-lang/tutorials)!
