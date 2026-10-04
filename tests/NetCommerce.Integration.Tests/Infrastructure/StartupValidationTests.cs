#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetCommerce.Kernel.Stripe;
using NetCommerce.Media.Infrastructure.Storage;
using NSubstitute;
using Shouldly;
using Xunit;

namespace NetCommerce.Integration.Tests.Infrastructure;

/// <summary>
///     Startup fail-closed validation: missing secrets or broken storage config
///     must stop the process at boot (naming the value), never fail per-request.
///     Pure unit tests — no containers, no fixture.
/// </summary>
public class StartupValidationTests
{
    private static IHostEnvironment Env(string environmentName)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(environmentName);
        return env;
    }

    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
            .Build();
    }

    private static S3OptionsValidator S3Validator(string environment, params (string Key, string? Value)[] values)
    {
        return new S3OptionsValidator(
            Env(environment),
            Config(values),
            NullLogger<S3OptionsValidator>.Instance);
    }

    private static S3Options S3(
        string? endpoint = "https://minio.example.com",
        string? bucket = "netcommerce",
        string? cdn = "https://cdn.example.com/netcommerce",
        string? accessKey = "AKIAEXAMPLE",
        string? secretKey = "secret-example")
    {
        return new S3Options
        {
            Endpoint = endpoint ?? string.Empty,
            AccessKey = accessKey ?? string.Empty,
            SecretKey = secretKey ?? string.Empty,
            BucketName = bucket ?? string.Empty,
            CdnBaseUrl = cdn ?? string.Empty
        };
    }

    [Fact]
    public void S3Validator_Accepts_Complete_NonLoopback_Config_In_Production()
    {
        var result = S3Validator(Environments.Production).Validate(null, S3());

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void S3Validator_Skips_Validation_When_Azure_Path_Is_Active()
    {
        // Same detection rule as MediaModule: a non-empty blobs connection
        // string selects Azure, so S3 values are irrelevant — including empty.
        var validator = S3Validator(
            Environments.Production,
            ("ConnectionStrings:blobs", "DefaultEndpointsProtocol=https;AccountName=x;"));

        var result = validator.Validate(null, S3(endpoint: "", bucket: "", cdn: "", accessKey: "", secretKey: ""));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void S3Validator_Rejects_Empty_Endpoint_When_S3_Path_Is_Active()
    {
        var result = S3Validator(Environments.Production).Validate(null, S3(endpoint: ""));

        result.Succeeded.ShouldBeFalse();
        (result.FailureMessage ?? "").ShouldContain("Storage:Endpoint");
    }

    [Fact]
    public void S3Validator_Rejects_Loopback_Endpoint_In_Production()
    {
        var result = S3Validator(Environments.Production)
            .Validate(null, S3(endpoint: "http://localhost:9000", cdn: "http://localhost:9000/netcommerce"));

        result.Succeeded.ShouldBeFalse();
        (result.FailureMessage ?? "").ShouldContain("localhost");
    }

    [Fact]
    public void S3Validator_Rejects_Missing_CdnBaseUrl()
    {
        var result = S3Validator(Environments.Production).Validate(null, S3(cdn: ""));

        result.Succeeded.ShouldBeFalse();
        (result.FailureMessage ?? "").ShouldContain("Storage:CdnBaseUrl");
    }

    [Fact]
    public void S3Validator_Rejects_Missing_Keys_Outside_Aws()
    {
        var result = S3Validator(Environments.Production)
            .Validate(null, S3(accessKey: "", secretKey: ""));

        result.Succeeded.ShouldBeFalse();
        (result.FailureMessage ?? "").ShouldContain("Storage:AccessKey");
    }

    [Fact]
    public void S3Validator_Allows_Missing_Keys_On_Aws_Iam_Roles()
    {
        var result = S3Validator(Environments.Production).Validate(null, S3(
            endpoint: "https://s3.eu-west-1.amazonaws.com",
            cdn: "https://cdn.example.com/netcommerce",
            accessKey: "",
            secretKey: ""));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void S3Validator_Ignores_Everything_Outside_Production_Like_Environments()
    {
        var result = S3Validator(Environments.Development)
            .Validate(null, S3(endpoint: "", bucket: "", cdn: ""));

        result.Succeeded.ShouldBeTrue();
    }

    private static StripeClientFactory StripeFactory(string? webhookSecret)
    {
        var options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_dummy",
            WebhookSecret = webhookSecret ?? string.Empty,
            TestMode = true
        });
        return new StripeClientFactory(options, NullLogger<StripeClientFactory>.Instance);
    }

    [Fact]
    public void VerifyWebhookSignature_Throws_Without_A_Secret_Instead_Of_Parsing()
    {
        // The old code returned EventUtility.ParseEvent(json) here, silently
        // accepting forged webhooks for any future caller.
        var factory = StripeFactory(webhookSecret: "");

        Should.Throw<InvalidOperationException>(() =>
            factory.VerifyWebhookSignature("{\"id\":\"evt_1\"}", "t=1,v1=abc"));
    }

    [Fact]
    public void VerifyWebhookSignature_Attempts_Verification_When_Secret_Is_Set()
    {
        var factory = StripeFactory(webhookSecret: "whsec_test");

        // Garbage signature against a configured secret must fail CLOSED
        // (StripeException), proving verification ran instead of a bare parse.
        Should.Throw<Stripe.StripeException>(() =>
            factory.VerifyWebhookSignature("{\"id\":\"evt_1\"}", "t=1,v1=abc"));
    }
}
