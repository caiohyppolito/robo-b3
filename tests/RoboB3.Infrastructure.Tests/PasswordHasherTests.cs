using RoboB3.Infrastructure.Security;

namespace RoboB3.Infrastructure.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Verifica_senha_correta_e_rejeita_incorreta()
    {
        var hash = PasswordHasher.Hash("senha-forte-123");

        Assert.True(PasswordHasher.Verify("senha-forte-123", hash));
        Assert.False(PasswordHasher.Verify("senha-errada", hash));
    }

    [Fact]
    public void Hashes_da_mesma_senha_sao_diferentes()
    {
        Assert.NotEqual(PasswordHasher.Hash("abc123456789"), PasswordHasher.Hash("abc123456789"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto-qualquer")]
    [InlineData("PBKDF2$10$abc$def")]
    [InlineData("PBKDF2$600000$@@@$@@@")]
    public void Rejeita_hash_invalido(string? hash)
    {
        Assert.False(PasswordHasher.Verify("qualquer", hash));
    }
}
