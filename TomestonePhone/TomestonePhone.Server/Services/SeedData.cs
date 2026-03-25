using System.Security.Cryptography;
using TomestonePhone.Server.Models;

namespace TomestonePhone.Server.Services;

public static class SeedData
{
    public static PersistedAppState Create(BootstrapOwnerOptions? bootstrapOwner = null)
    {
        var state = new PersistedAppState
        {
            NextPhoneNumber = 5550100000L,
        };

        if (bootstrapOwner is null
            || string.IsNullOrWhiteSpace(bootstrapOwner.Username)
            || string.IsNullOrWhiteSpace(bootstrapOwner.Password))
        {
            return state;
        }

        var salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        state.NextPhoneNumber++;
        var ownerId = Guid.NewGuid();
        var displayName = string.IsNullOrWhiteSpace(bootstrapOwner.DisplayName)
            ? bootstrapOwner.Username
            : bootstrapOwner.DisplayName;

        state.Accounts.Add(new PersistedAccount
        {
            Id = ownerId,
            Username = bootstrapOwner.Username,
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(bootstrapOwner.Password, salt),
            PasswordSalt = salt,
            PhoneNumber = state.NextPhoneNumber.ToString("0000000000"),
            Role = "Owner",
            Status = "Active",
            AcceptedLegalTermsVersion = "bootstrap",
            AcceptedLegalTermsAtUtc = DateTimeOffset.UtcNow,
            AcceptedPrivacyPolicyVersion = "bootstrap",
            AcceptedPrivacyPolicyAtUtc = DateTimeOffset.UtcNow,
            LastKnownGameIdentity = string.IsNullOrWhiteSpace(bootstrapOwner.CharacterName) || string.IsNullOrWhiteSpace(bootstrapOwner.WorldName)
                ? null
                : new PersistedGameIdentity
                {
                    CharacterName = bootstrapOwner.CharacterName,
                    WorldName = bootstrapOwner.WorldName,
                    FullHandle = $"{bootstrapOwner.CharacterName}@{bootstrapOwner.WorldName}",
                },
        });

        state.AuditLogs.Add(new PersistedAuditLog
        {
            Id = Guid.NewGuid(),
            ActorAccountId = ownerId,
            ActorDisplayName = displayName,
            EventType = "BootstrapOwnerCreated",
            Summary = $"Bootstrap owner account {bootstrapOwner.Username} created.",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        return state;
    }
}
