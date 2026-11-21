namespace MorningInk.Domain;

public class User
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string Language { get; set; } = "de";

    public UserRole Role { get; set; } = UserRole.User;
}
