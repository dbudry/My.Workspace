using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using My.Functions;
using Xunit;

namespace My.Tests.Routing
{
    /// <summary>
    /// Guards the fix for the GET /trackedtasks/range 404: Azure Functions matched the
    /// single-segment {id} route for /trackedtasks/range (and /active), so those literal
    /// endpoints 404'd via GetById. The {id} route now carries a regex constraint that
    /// excludes those words. This test reads the real route off the attribute (so it can't
    /// drift from the source) and checks the constraint behaves.
    /// </summary>
    public class TrackedTaskRouteConstraintTests
    {
        private static string GetRoute(string methodName)
        {
            var method = typeof(TrackedTaskFunctions).GetMethod(methodName)
                ?? throw new InvalidOperationException($"{methodName} not found");
            var trigger = method.GetParameters()
                .Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
                .FirstOrDefault(a => a != null)
                ?? throw new InvalidOperationException($"{methodName} has no HttpTrigger");
            return trigger.Route!;
        }

        [Fact]
        public void GetById_route_does_not_swallow_literal_sibling_routes()
        {
            var route = GetRoute("GetTrackedTaskAsync");

            // Extract the inline regex constraint from: trackedtasks/{id:regex(<pattern>)}
            const string marker = "regex(";
            var start = route.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, $"GET {{id}} route must carry a regex constraint, was: {route}");
            start += marker.Length;
            var end = route.LastIndexOf(")}", StringComparison.Ordinal);
            var pattern = route.Substring(start, end - start);

            var rx = new Regex(pattern);
            Assert.False(rx.IsMatch("range"), "'range' must fall through to GetTrackedTasksRange");
            Assert.False(rx.IsMatch("active"), "'active' must fall through to GetActiveTrackedTasks");
            Assert.True(rx.IsMatch("d1e2f3a4-b5c6-7890-d1e2-f3a4b5c67890"), "real GUID ids must still match {id}");
            Assert.True(rx.IsMatch("any-normal-id"), "non-reserved ids must still match {id}");
        }
    }
}
