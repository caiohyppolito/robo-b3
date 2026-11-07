namespace RoboB3.Domain.Common;

internal static class Guard
{
    public static string NotBlank(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} é obrigatório.");
        }

        return value.Trim();
    }

    public static decimal Positive(decimal value, string name)
    {
        if (value <= 0)
        {
            throw new DomainException($"{name} deve ser maior que zero.");
        }

        return value;
    }

    public static decimal NotNegative(decimal value, string name)
    {
        if (value < 0)
        {
            throw new DomainException($"{name} não pode ser negativo.");
        }

        return value;
    }

    public static decimal Percentage(decimal value, string name)
    {
        if (value <= 0 || value > 100)
        {
            throw new DomainException($"{name} deve estar entre 0 (exclusivo) e 100.");
        }

        return value;
    }

    public static int Positive(int value, string name)
    {
        if (value <= 0)
        {
            throw new DomainException($"{name} deve ser maior que zero.");
        }

        return value;
    }
}
