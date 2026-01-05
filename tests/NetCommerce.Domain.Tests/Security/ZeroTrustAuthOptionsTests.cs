#region

using NetCommerce.SharedKernel.Infrastructure.Security.Authentication;

#endregion

namespace NetCommerce.Domain.Tests.Security;

/// <summary>
///     Unit tests for ZeroTrustAuthOptions.
///     Verifies URL construction and configuration binding.
/// </summary>
public class ZeroTrustAuthOptionsTests
{
    [Fact]
    public void RealmUrl_WithValidAuthorityAndRealm_ConstructsCorrectUrl()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "https://keycloak.example.com", Realm = "netcommerce" };

        // Act & Assert
        options.RealmUrl.ShouldBe("https://keycloak.example.com/realms/netcommerce");
    }

    [Fact]
    public void RealmUrl_WithTrailingSlash_HandlesCorrectly()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "https://keycloak.example.com/", Realm = "netcommerce" };

        // Act & Assert
        options.RealmUrl.ShouldBe("https://keycloak.example.com/realms/netcommerce");
    }

    [Fact]
    public void RealmUrl_WithEmptyAuthority_ReturnsEmptyString()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "", Realm = "netcommerce" };

        // Act & Assert
        options.RealmUrl.ShouldBeEmpty();
    }

    [Fact]
    public void RealmUrl_WithEmptyRealm_ReturnsEmptyString()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "https://keycloak.example.com", Realm = "" };

        // Act & Assert
        options.RealmUrl.ShouldBeEmpty();
    }

    [Fact]
    public void TokenEndpoint_WithValidRealmUrl_ConstructsCorrectUrl()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "https://keycloak.example.com", Realm = "netcommerce" };

        // Act & Assert
        options.TokenEndpoint.ShouldBe(
            "https://keycloak.example.com/realms/netcommerce/protocol/openid-connect/token");
    }

    [Fact]
    public void IntrospectionEndpoint_WithValidRealmUrl_ConstructsCorrectUrl()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "https://keycloak.example.com", Realm = "netcommerce" };

        // Act & Assert
        options.IntrospectionEndpoint.ShouldBe(
            "https://keycloak.example.com/realms/netcommerce/protocol/openid-connect/token/introspect");
    }

    [Fact]
    public void TokenEndpoint_WithEmptyRealmUrl_ReturnsEmptyString()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "", Realm = "" };

        // Act & Assert
        options.TokenEndpoint.ShouldBeEmpty();
    }

    [Fact]
    public void IntrospectionEndpoint_WithEmptyRealmUrl_ReturnsEmptyString()
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = "", Realm = "" };

        // Act & Assert
        options.IntrospectionEndpoint.ShouldBeEmpty();
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new ZeroTrustAuthOptions();

        // Assert
        options.Audience.ShouldBe("netcommerce-api");
        options.ApiScope.ShouldBe("netcommerce.api");
        options.ClientId.ShouldBe("netcommerce-api");
        options.ClientSecret.ShouldBeEmpty();
        options.IntrospectionEnabled.ShouldBeFalse();
        options.IntrospectionCacheSeconds.ShouldBe(30);
        options.TokenExchangeEnabled.ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://localhost:8080", "test", "http://localhost:8080/realms/test")]
    [InlineData("https://auth.prod.example.com", "production", "https://auth.prod.example.com/realms/production")]
    [InlineData("http://keycloak:8080/auth", "dev", "http://keycloak:8080/auth/realms/dev")]
    public void RealmUrl_VariousConfigurations_ConstructsCorrectly(
        string authority, string realm, string expected)
    {
        // Arrange
        var options = new ZeroTrustAuthOptions { Authority = authority, Realm = realm };

        // Act & Assert
        options.RealmUrl.ShouldBe(expected);
    }
}
