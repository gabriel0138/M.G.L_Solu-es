using FluentAssertions;
using MglConnect.Web.Services;
using Npgsql;
using Xunit;

namespace MglConnect.Tests;

public class PostgresConnectionStringParserTests
{
    [Fact]
    public void Parse_DecodesEscapedCredentialsAndUsesSsl()
    {
        var result = PostgresConnectionStringParser.Parse(
            "postgresql://mgl-user:p%3Ass%40word@db.example.test:5433/logistics");
        var connection = new NpgsqlConnectionStringBuilder(result);

        connection.Host.Should().Be("db.example.test");
        connection.Port.Should().Be(5433);
        connection.Database.Should().Be("logistics");
        connection.Username.Should().Be("mgl-user");
        connection.Password.Should().Be("p:ss@word");
        connection.SslMode.Should().Be(SslMode.Require);
    }

    [Fact]
    public void Parse_PreservesStandardConnectionStrings()
    {
        const string connectionString = "Host=db.example.test;Database=logistics;Username=mgl-user";

        PostgresConnectionStringParser.Parse(connectionString).Should().Be(connectionString);
    }
}
