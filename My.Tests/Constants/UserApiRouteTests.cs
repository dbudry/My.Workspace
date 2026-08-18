using My.Shared.Constants;
using Xunit;

namespace My.Tests.ApiRoutes;

public class UserApiRouteTests
{
    [Fact]
    public void ActionPath_builds_setactive_route()
    {
        Assert.Equal("users/abc/setactive", Constants.API.User.ActionPath("abc", "setactive"));
    }

    [Fact]
    public void ById_builds_delete_route()
    {
        Assert.Equal("users/abc", Constants.API.User.ById("abc"));
    }
}