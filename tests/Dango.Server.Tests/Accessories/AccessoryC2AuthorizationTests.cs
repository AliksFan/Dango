using Dango.Server.Tests.Accounts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dango.Server.Tests.Accessories;

[TestClass]
public sealed class AccessoryC2AuthorizationTests
{
    [TestMethod]
    public async Task OldStageApprovalCannotStartC2ResourcesOrReadItsTarget()
    {
        var oldB1 = Environment.GetEnvironmentVariable("DANGO_BACKEND_B1_TEST_APPROVED");
        var oldB5 = Environment.GetEnvironmentVariable("DANGO_BACKEND_B5A_TEST_APPROVED");
        var oldC2 = Environment.GetEnvironmentVariable("DANGO_BACKEND_C2_TEST_APPROVED");
        try
        {
            Environment.SetEnvironmentVariable("DANGO_BACKEND_B1_TEST_APPROVED", "1");
            Environment.SetEnvironmentVariable("DANGO_BACKEND_B5A_TEST_APPROVED", "1");
            Environment.SetEnvironmentVariable("DANGO_BACKEND_C2_TEST_APPROVED", "0");
            await Assert.ThrowsExactlyAsync<AssertInconclusiveException>(async () =>
            {
                await using var fixture = await FormalTestFixture.CreateAsync(startHttp: true, requireC2Approval: true);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("DANGO_BACKEND_B1_TEST_APPROVED", oldB1);
            Environment.SetEnvironmentVariable("DANGO_BACKEND_B5A_TEST_APPROVED", oldB5);
            Environment.SetEnvironmentVariable("DANGO_BACKEND_C2_TEST_APPROVED", oldC2);
        }
    }
}
