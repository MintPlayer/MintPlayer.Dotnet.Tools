public class ApplicationBuilder
{
    public string Name { get; set; }

}

public class DatabaseBuilder
{
    public List<ConnectionStringConfig> ConnectionStrings { get; set; }
}

public class ConnectionStringBuilder
{
    public string Name { get; set; }
    public string Server { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}

public class LoggerBuilder
{
    public string Path { get; set; }
}

// --------------------------------------------------------------

public class ApplicationConfig
{
    public string Name { get; }
}

public class DatabaseConfig
{
    public IReadOnlyList<ConnectionStringConfig> ConnectionStrings { get; }
}

public class ConnectionStringConfig
{
    public string Name { get; }
    public string Server { get; }
    public string Username { get; }
    public string Password { get; }
}

public class LoggerConfig
{
    public string Path { get; }
}

var app = new ApplicationBuilder()