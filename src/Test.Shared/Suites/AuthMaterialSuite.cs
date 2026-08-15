namespace Test.Shared.Suites
{
    using EasySlack;
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Exercises <see cref="SlackAuthMaterial"/> token validation and sanitization.
    /// </summary>
    public static class AuthMaterialSuite
    {
        private const string SuiteId = "AuthMaterial";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Sync("ValidTokens", "Valid xoxb/xapp tokens are accepted", () =>
                {
                    SlackAuthMaterial auth = new SlackAuthMaterial("xoxb-valid", "xapp-valid");
                    Check.Equal("xoxb-valid", auth.BotToken, "bot token");
                    Check.Equal("xapp-valid", auth.AppToken, "app token");
                }),

                Sync("DefaultCtorEmptyTokens", "Default constructor leaves tokens empty", () =>
                {
                    SlackAuthMaterial auth = new SlackAuthMaterial();
                    Check.Equal(string.Empty, auth.BotToken, "bot token");
                    Check.Equal(string.Empty, auth.AppToken, "app token");
                }),

                Sync("BotTokenWrongPrefix", "Bot token with wrong prefix throws ArgumentException", () =>
                {
                    Check.Throws<ArgumentException>(() => new SlackAuthMaterial("bad-token", "xapp-valid"), "bot token prefix");
                }),

                Sync("AppTokenWrongPrefix", "App token with wrong prefix throws ArgumentException", () =>
                {
                    Check.Throws<ArgumentException>(() => new SlackAuthMaterial("xoxb-valid", "bad-token"), "app token prefix");
                }),

                Sync("BotTokenNullThrows", "Null bot token throws ArgumentNullException", () =>
                {
                    Check.Throws<ArgumentNullException>(() => new SlackAuthMaterial(null!, "xapp-valid"), "null bot token");
                }),

                Sync("AppTokenNullThrows", "Null app token throws ArgumentNullException", () =>
                {
                    Check.Throws<ArgumentNullException>(() => new SlackAuthMaterial("xoxb-valid", null!), "null app token");
                }),

                Sync("BotTokenWhitespaceThrows", "Whitespace bot token throws ArgumentNullException", () =>
                {
                    Check.Throws<ArgumentNullException>(() => new SlackAuthMaterial("   ", "xapp-valid"), "whitespace bot token");
                }),

                Sync("PrefixIsCaseInsensitive", "Uppercase token prefixes are accepted", () =>
                {
                    SlackAuthMaterial auth = new SlackAuthMaterial("XOXB-upper", "XAPP-upper");
                    Check.Equal("XOXB-upper", auth.BotToken, "bot token");
                    Check.Equal("XAPP-upper", auth.AppToken, "app token");
                }),

                Sync("TokensAreTrimmed", "Surrounding whitespace is trimmed from tokens", () =>
                {
                    SlackAuthMaterial auth = new SlackAuthMaterial("  xoxb-trim  ", "  xapp-trim  ");
                    Check.Equal("xoxb-trim", auth.BotToken, "bot token");
                    Check.Equal("xapp-trim", auth.AppToken, "app token");
                }),

                Sync("SetterRejectsInvalidPrefix", "Property setter validates prefix", () =>
                {
                    SlackAuthMaterial auth = new SlackAuthMaterial();
                    Check.Throws<ArgumentException>(() => auth.BotToken = "nope", "bot setter");
                    Check.Throws<ArgumentException>(() => auth.AppToken = "nope", "app setter");
                }),
            };

            return new TestSuiteDescriptor(SuiteId, "Slack Auth Material", cases);
        }

        private static TestCaseDescriptor Sync(string caseId, string displayName, Action body)
        {
            return new TestCaseDescriptor(SuiteId, caseId, displayName, _ =>
            {
                body();
                return Task.CompletedTask;
            });
        }
    }
}
