namespace Notification.Infrastructure.Services.Email;

using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>Uses the same owner-supplied key ring on every shared-database replica.</summary>
public static class OtpDataProtectionConfiguration
{
    public static IServiceCollection AddOtpDataProtection(this IServiceCollection services, IConfiguration configuration)
    {
        var encoded = configuration["OtpProtection:SharedKeyRingBase64"]?.Trim();
        if (configuration.GetValue<bool>("OtpProtection:RequireSharedKeyRing") && string.IsNullOrEmpty(encoded))
            throw new InvalidOperationException("Shared OTP keys are required. Install the private configuration supplied by the service owner.");

        // Validate before registering or starting a provider; never log the private bundle.
        var shared = string.IsNullOrEmpty(encoded) ? null : SharedOtpXmlRepository.Parse(encoded, DateTimeOffset.UtcNow);
        var protection = services.AddDataProtection().SetApplicationName("Nexus.Notification.Otp");
        if (shared is not null)
        {
            protection.DisableAutomaticKeyGeneration();
            services.Configure<KeyManagementOptions>(options => options.XmlRepository = shared);
        }
        else
        {
            var directory = configuration["OtpProtection:KeyRingPath"];
            if (!string.IsNullOrWhiteSpace(directory)) protection.PersistKeysToFileSystem(new DirectoryInfo(directory));
        }
        return services;
    }
}

/// <summary>Private, bounded, read-only Data Protection key storage from configuration.</summary>
public sealed class SharedOtpXmlRepository : IXmlRepository
{
    private readonly XElement[] _keys;
    private SharedOtpXmlRepository(XElement[] keys) => _keys = keys;

    public static SharedOtpXmlRepository Parse(string encoded, DateTimeOffset now)
    {
        try
        {
            if (encoded.Length > 131072) throw new FormatException();
            var xml = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
            using var text = new StringReader(xml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 65536
            });
            var root = XElement.Load(reader);
            if (root.Name != "keyRing" || root.Attributes().Any()) throw new FormatException();
            var keys = root.Elements().ToArray();
            if (keys.Length is < 1 or > 32) throw new FormatException();
            var ids = new HashSet<Guid>(); var active = false;
            foreach (var key in keys)
            {
                if (key.Name != "key" || (string?)key.Attribute("version") != "1" ||
                    !Guid.TryParse((string?)key.Attribute("id"), out var id) || id == Guid.Empty || !ids.Add(id))
                    throw new FormatException();
                if (key.Descendants().Any(element => element.Name.Namespace != XNamespace.None ||
                    element.Name.LocalName == "encryptedSecret" || element.Attribute("decryptorType") is not null))
                    throw new FormatException();
                var created = Date(key, "creationDate"); var activation = Date(key, "activationDate");
                var expiration = Date(key, "expirationDate");
                if (activation >= expiration || created >= expiration) throw new FormatException();
                active |= activation <= now && expiration > now;
                var outer = key.Element("descriptor") ?? throw new FormatException();
                var type = ((string?)outer.Attribute("deserializerType") ?? "").Split(',');
                if (type.Length < 2 || type[0].Trim() != "Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel.AuthenticatedEncryptorDescriptorDeserializer" ||
                    type[1].Trim() != "Microsoft.AspNetCore.DataProtection") throw new FormatException();
                var descriptor = outer.Element("descriptor") ?? throw new FormatException();
                if ((string?)descriptor.Element("encryption")?.Attribute("algorithm") != "AES_256_CBC" ||
                    (string?)descriptor.Element("validation")?.Attribute("algorithm") != "HMACSHA256" ||
                    Convert.FromBase64String(descriptor.Element("masterKey")?.Element("value")?.Value ?? "").Length != 64)
                    throw new FormatException();
            }
            if (!active) throw new FormatException();
            return new(keys.Select(key => new XElement(key)).ToArray());
        }
        catch (Exception exception) when (exception is FormatException or XmlException or DecoderFallbackException or InvalidOperationException)
        {
            throw new InvalidOperationException("Shared OTP key configuration is invalid or has no active supported key. Request an updated private configuration from the service owner.");
        }
    }

    private static DateTimeOffset Date(XElement key, string name)
        => DateTimeOffset.TryParse(key.Element(name)?.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value : throw new FormatException();

    public IReadOnlyCollection<XElement> GetAllElements() => _keys.Select(key => new XElement(key)).ToArray();
    public void StoreElement(XElement element, string friendlyName)
        => throw new InvalidOperationException("Shared OTP keys are read-only. Rotation must be distributed through private configuration.");
}
