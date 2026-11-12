namespace Notification.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Notification.Application.Interfaces;
using Notification.Application.Services.Deliveries;
using Notification.Application.Services.Email;
using Notification.Domain.Enums;
using Notification.Infrastructure.Persistence;
using Notification.Infrastructure.Services.Email;
using Xunit;

public sealed class SharedOtpKeyRingTests
{
    internal static (string Bundle, ServiceProvider Original) Material()
    {
        var storage = new GeneratingRepository();
        var services = new ServiceCollection().AddLogging();
        services.AddDataProtection().SetApplicationName("Nexus.Notification.Otp").DisableAutomaticKeyGeneration();
        services.Configure<KeyManagementOptions>(options => options.XmlRepository = storage);
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKeyManager>().CreateNewKey(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(90));
        return (Encode(new XElement("keyRing", storage.GetAllElements())), provider);
    }
    internal static string Encode(XElement xml) => Convert.ToBase64String(Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting)));
    internal static ServiceProvider Replica(string bundle, string? directory = null)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddOtpDataProtection(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OtpProtection:SharedKeyRingBase64"] = bundle,
            ["OtpProtection:RequireSharedKeyRing"] = "true",
            ["OtpProtection:KeyRingPath"] = directory
        }).Build());
        return services.BuildServiceProvider();
    }
    internal static IOtpPayloadProtector Protector(ServiceProvider provider)
        => new DataProtectionOtpPayloadProtector(provider.GetRequiredService<IDataProtectionProvider>());

    [Fact]
    public void Independent_replicas_and_restart_share_payload_and_fingerprint_without_shared_disk()
    {
        var (bundle, original) = Material(); using var seed = original;
        using var first = Replica(bundle, "unused-first-machine-directory");
        using var second = Replica(bundle, "unused-second-machine-directory");
        var a = Protector(first); var b = Protector(second);
        Assert.Equal("payload", b.UnprotectPayload(a.ProtectPayload("payload")));
        Assert.Equal("proof", a.UnprotectFingerprint(b.ProtectFingerprint("proof")));
        Assert.Throws<CryptographicException>(() => b.UnprotectFingerprint(a.ProtectPayload("payload")));
        using var restart = Replica(bundle);
        Assert.Equal("after restart", Protector(restart).UnprotectPayload(a.ProtectPayload("after restart")));
        Assert.False(first.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeyManagementOptions>>().Value.AutoGenerateKeys);
    }

    [Fact]
    public void Existing_DataProtection_ciphertext_remains_readable_and_repository_is_immutable()
    {
        var (bundle, original) = Material(); using var seed = original;
        var legacy = Protector(seed); var payload = legacy.ProtectPayload("retained payload");
        using var replica = Replica(bundle);
        Assert.Equal("retained payload", Protector(replica).UnprotectPayload(payload));
        var repository = SharedOtpXmlRepository.Parse(bundle, DateTimeOffset.UtcNow);
        var returned = repository.GetAllElements().Single(); returned.RemoveNodes();
        Assert.NotEmpty(repository.GetAllElements().Single().Elements());
        Assert.Throws<InvalidOperationException>(() => repository.StoreElement(new XElement("key"), "new-key"));
    }

    [Fact]
    public void Missing_required_bundle_fails_before_any_worker_starts()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["OtpProtection:RequireSharedKeyRing"] = "true" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddOtpDataProtection(configuration));
    }

    [Theory]
    [InlineData("bad-base64")]
    [InlineData("empty")]
    [InlineData("wrong-root")]
    [InlineData("malformed")]
    [InlineData("doctype")]
    [InlineData("duplicate")]
    [InlineData("custom-deserializer")]
    [InlineData("invalid-master-key")]
    [InlineData("machine-encrypted")]
    [InlineData("expired-only")]
    [InlineData("future-only")]
    [InlineData("oversized")]
    public void Invalid_or_unsafe_bundles_fail_without_echoing_private_configuration(string mutation)
    {
        var (bundle, original) = Material(); using var seed = original;
        var xml = XElement.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(bundle)));
        var key = xml.Element("key")!;
        switch (mutation)
        {
            case "bad-base64": bundle = "private-not-base64"; break;
            case "empty": xml.RemoveNodes(); bundle = Encode(xml); break;
            case "wrong-root": xml.Name = "unexpected"; bundle = Encode(xml); break;
            case "malformed": bundle = Convert.ToBase64String(Encoding.UTF8.GetBytes("<keyRing>")); break;
            case "doctype": bundle = Convert.ToBase64String(Encoding.UTF8.GetBytes("<!DOCTYPE keyRing [<!ENTITY test SYSTEM 'file:///private-key'>]><keyRing>&test;</keyRing>")); break;
            case "duplicate": xml.Add(new XElement(key)); bundle = Encode(xml); break;
            case "custom-deserializer": key.Element("descriptor")!.SetAttributeValue("deserializerType", "Unexpected.Type, Unexpected.Assembly"); bundle = Encode(xml); break;
            case "invalid-master-key": key.Descendants("value").Single().Value = Convert.ToBase64String(new byte[3]); bundle = Encode(xml); break;
            case "machine-encrypted": key.Add(new XElement("encryptedSecret")); bundle = Encode(xml); break;
            case "expired-only": key.Element("expirationDate")!.Value = DateTimeOffset.UtcNow.AddSeconds(-1).ToString("o"); bundle = Encode(xml); break;
            case "future-only": key.Element("activationDate")!.Value = DateTimeOffset.UtcNow.AddDays(1).ToString("o"); bundle = Encode(xml); break;
            case "oversized": bundle = new string('A', 131073); break;
        }
        var error = Assert.Throws<InvalidOperationException>(() => SharedOtpXmlRepository.Parse(bundle, DateTimeOffset.UtcNow));
        Assert.DoesNotContain(bundle, error.Message);
        Assert.Null(error.InnerException);
    }

    private sealed class GeneratingRepository : IXmlRepository
    {
        private readonly List<XElement> _elements = [];
        public IReadOnlyCollection<XElement> GetAllElements() => _elements.Select(element => new XElement(element)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => _elements.Add(new XElement(element));
    }
}

public sealed class SharedOtpWorkerIntegrationTests : IAsyncLifetime
{
    private string? _admin, _database, _connection;
    public async Task InitializeAsync()
    {
        _admin = Environment.GetEnvironmentVariable("NOTIFICATION_TEST_DB");
        if (string.IsNullOrWhiteSpace(_admin)) return;
        if (new NpgsqlConnectionStringBuilder(_admin).Host is not ("127.0.0.1" or "localhost"))
            throw new InvalidOperationException("This test requires an isolated loopback PostgreSQL instance.");
        _database = "shared_otp_keys_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("CREATE DATABASE \"" + _database + "\"", admin).ExecuteNonQueryAsync();
        _connection = new NpgsqlConnectionStringBuilder(_admin) { Database = _database }.ConnectionString;
        await using var db = Db(); await db.Database.MigrateAsync();
    }
    private AppDbContext Db() => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);
    public async Task DisposeAsync()
    {
        if (_database is null) return;
        await using var admin = new NpgsqlConnection(_admin); await admin.OpenAsync();
        await new NpgsqlCommand("DROP DATABASE \"" + _database + "\" WITH (FORCE)", admin).ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task Another_machine_worker_sends_all_OTP_purposes_and_replays_same_fingerprint()
    {
        var (bundle, original) = SharedOtpKeyRingTests.Material(); using var seed = original;
        using var machineA = SharedOtpKeyRingTests.Replica(bundle);
        using var machineB = SharedOtpKeyRingTests.Replica(bundle);
        var sender = new Sender(); var settings = new OtpWorkerSettings();
        foreach (var purpose in new[] { "registration", "password_reset", "seller_registration" })
        {
            var now = DateTimeOffset.UtcNow;
            var request = new SendOtpEmailRequest { RequestId = Guid.NewGuid(), UserId = Guid.NewGuid(), Email = "shared@example.test", FullName = "Test", OtpCode = "817263", Purpose = purpose, IssuedAt = now, ExpiresAt = now.AddMinutes(5) };
            await using (var producerDb = Db())
                await new OtpEmailDispatchService(producerDb, SharedOtpKeyRingTests.Protector(machineA), sender, settings, TimeProvider.System).EnqueueAsync(request);
            await using (var workerDb = Db())
                Assert.True(await new OtpEmailDispatchService(workerDb, SharedOtpKeyRingTests.Protector(machineB), sender, settings, TimeProvider.System).ProcessNextAsync());
            await using var verify = Db(); var row = await verify.OtpEmailDispatches.AsNoTracking().SingleAsync(job => job.Id == request.RequestId);
            Assert.Equal(OtpDispatchStatus.ProviderAccepted, row.Status); Assert.Equal(1, row.AttemptCount); Assert.Null(row.ErrorCode); Assert.Null(row.ProtectedPayload);
            var replay = await new OtpEmailDispatchService(verify, SharedOtpKeyRingTests.Protector(machineB), sender, settings, TimeProvider.System).EnqueueAsync(request);
            Assert.True(replay.IsReplay); Assert.Equal("ProviderAccepted", replay.Status);
        }
        Assert.Equal(3, sender.Calls);
    }
    private sealed class Sender : IEmailDeliverySender
    {
        public int Calls;
        public Task<EmailDeliveryResult> SendAsync(EmailDeliveryMessage request, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(EmailDeliveryResult.Sent()); }
    }
}
