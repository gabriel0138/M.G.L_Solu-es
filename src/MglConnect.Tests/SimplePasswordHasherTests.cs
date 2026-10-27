using Xunit;
using MglConnect.Web.Services;
using FluentAssertions;

namespace MglConnect.Tests;

public class SimplePasswordHasherTests
{
    private readonly SimplePasswordHasher _hasher;

    public SimplePasswordHasherTests()
    {
        _hasher = new SimplePasswordHasher();
    }

    [Fact]
    public void HashPassword_DeveGerarHashDiferenteParaMesmoInput()
    {
        // Act
        var hash1 = _hasher.HashPassword("senha123");
        var hash2 = _hasher.HashPassword("senha123");

        // Assert
        hash1.Should().NotBe(hash2, "Cada chamada deve gerar um salt único");
        hash1.Should().NotBeEmpty("Hash não deve estar vazio");
    }

    [Fact]
    public void VerifyPassword_DeveRetornarVerdadeiroParaSenhaCorreta()
    {
        // Arrange
        var password = "admin123";
        var hash = _hasher.HashPassword(password);

        // Act
        var result = _hasher.VerifyPassword(password, hash);

        // Assert
        result.Should().BeTrue("Senha correta deve ser verificada");
    }

    [Fact]
    public void VerifyPassword_DeveRetornarFalsoParaSenhaIncorreta()
    {
        // Arrange
        var password = "admin123";
        var wrongPassword = "senhaErrada";
        var hash = _hasher.HashPassword(password);

        // Act
        var result = _hasher.VerifyPassword(wrongPassword, hash);

        // Assert
        result.Should().BeFalse("Senha incorreta deve falhar na verificação");
    }

    [Fact]
    public void HashPassword_DeveAceitarSenhaVazia()
    {
        // Act
        var hash = _hasher.HashPassword(string.Empty);

        // Assert - hash vazia é aceita pelo sistema (caso edge case)
        hash.Should().BeEmpty("Hash de senha vazia deve retornar string vazia");
    }

    [Fact]
    public void VerifyPassword_DeveRetornarFalsoParaHashVazio()
    {
        // Act
        var result = _hasher.VerifyPassword("qualquerCoisa", string.Empty);

        // Assert
        result.Should().BeFalse();
    }
}