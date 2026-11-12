using ArgipApi.Demo;

namespace ArgipApi.Client.Tests;

public class ArgumentsTests
{
    [Fact]
    public void ParsesPositionalFlagsAndOptions()
    {
        var a = Arguments.Parse(["products", "list", "--page", "2", "--all-states", "--filter", "DIN 931"]);

        Assert.Equal(["products", "list"], a.Positional);
        Assert.True(a.Flag("all-states"));
        Assert.Equal(2, a.Option("page", 0));
        Assert.Equal("DIN 931", a.Option("filter"));
    }

    [Theory]
    [InlineData("--page")]
    [InlineData("--help")]
    [InlineData("--bogus", "1")]
    public void UnknownOptionOrMissingValueIsAUsageError(params string[] options)
    {
        Assert.Throws<UsageException>(() => Arguments.Parse(["products", "list", .. options]));
    }

    [Fact]
    public void AddressCompanyIsRequired()
    {
        var a = Arguments.Parse(["addresses", "put", "--name", "Home"]);

        var error = Assert.Throws<UsageException>(() => a.RequiredOption("company"));
        Assert.Equal("Missing required option --company.", error.Message);
    }

    [Fact]
    public void WriteResultPrintsMessageOnItsOwnLineOnlyWithoutJson()
    {
        const string message = "Testing phase: the data is valid and would be saved in the production phase.";
        var model = new ShippingAddressModel { ShippingAddressId = 0, Message = message };

        var plain = Capture(() => Output.WriteResult(model, model.Message, json: false));
        var asJson = Capture(() => Output.WriteResult(model, model.Message, json: true));
        var production = Capture(() => Output.WriteResult(model with { Message = null }, null, json: false));

        Assert.EndsWith(message + Environment.NewLine, plain);
        Assert.Equal(2, plain.Split(message).Length - 1);
        Assert.Equal(1, asJson.Split(message).Length - 1);
        Assert.EndsWith("}" + Environment.NewLine, asJson);
        Assert.EndsWith("}" + Environment.NewLine, production);
    }

    private static string Capture(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try { action(); }
        finally { Console.SetOut(original); }
        return writer.ToString();
    }

    [Fact]
    public void OptionFollowedByAnotherOptionHasNoValue()
    {
        var error = Assert.Throws<UsageException>(() => Arguments.Parse(["products", "list", "--page", "--size", "5"]));

        Assert.Contains("--page", error.Message);
    }

    [Fact]
    public void NonNumericNumberIsAUsageError()
    {
        var a = Arguments.Parse(["products", "list", "--page", "abc"]);

        Assert.Throws<UsageException>(() => a.Option("page", 0));
    }

    [Theory]
    [InlineData("--page", "-1")]
    [InlineData("--size", "0")]
    [InlineData("--size", "1001")]
    public void PagingOutOfRangeIsAUsageError(string option, string value)
    {
        var a = Arguments.Parse(["products", "list", option, value]);

        Assert.Throws<UsageException>(() => a.Paging());
    }

    [Fact]
    public void PagingDefaultsAndBounds()
    {
        Assert.Equal((0, 20), Arguments.Parse(["products", "list"]).Paging());
        Assert.Equal((3, 1000), Arguments.Parse(["products", "list", "--page", "3", "--size", "1000"]).Paging());
        Assert.Equal((0, 1), Arguments.Parse(["products", "list", "--size", "1"]).Paging());
    }
}
