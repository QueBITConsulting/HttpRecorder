﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using QueBIT.HttpRecorder.Context;
using QueBIT.HttpRecorder.Tests.Server;
using Xunit;

namespace QueBIT.HttpRecorder.Tests
{
    [Collection(ServerCollection.Name)]
    public class ContextTests
    {
        private readonly ServerFixture _fixture;

        public ContextTests(ServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ItShouldWorkWithHttpRecorderContext()
        {
            var services = new ServiceCollection();
            services
                .AddHttpRecorderContextSupport()
                .AddHttpClient(
                    "TheClient",
                    options =>
                    {
                        options.BaseAddress = _fixture.ServerUri;
                    });

            HttpResponseMessage passthroughResponse = null;
            using (var context = new HttpRecorderContext((sp, builder) => new HttpRecorderConfiguration
            {
                Mode = HttpRecorderMode.Record,
                InteractionName = nameof(ItShouldWorkWithHttpRecorderContext),
            }))
            {
                var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("TheClient");
                passthroughResponse = await client.GetAsync(ApiController.JsonUri);
                passthroughResponse.EnsureSuccessStatusCode();
            }

            using (var context = new HttpRecorderContext((sp, builder) => new HttpRecorderConfiguration
            {
                Mode = HttpRecorderMode.Replay,
                InteractionName = nameof(ItShouldWorkWithHttpRecorderContext),
            }))
            {
                var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("TheClient");
                var response = await client.GetAsync(ApiController.JsonUri);
                response.EnsureSuccessStatusCode();
                response.Should().BeEquivalentTo(passthroughResponse);
            }
        }

        [Fact]
        public async Task ItShouldWorkWithHttpRecorderContextWhenNotRecording()
        {
            var services = new ServiceCollection();
            services
                .AddHttpRecorderContextSupport()
                .AddHttpClient(
                    "TheClient",
                    options =>
                    {
                        options.BaseAddress = _fixture.ServerUri;
                    });

            HttpResponseMessage passthroughResponse = null;
            using (var context = new HttpRecorderContext((sp, builder) => new HttpRecorderConfiguration
            {
                Enabled = false,
                Mode = HttpRecorderMode.Record,
                InteractionName = nameof(ItShouldWorkWithHttpRecorderContextWhenNotRecording),
            }))
            {
                var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("TheClient");
                passthroughResponse = await client.GetAsync(ApiController.JsonUri);
                passthroughResponse.EnsureSuccessStatusCode();
            }

            using (var context = new HttpRecorderContext((sp, builder) => new HttpRecorderConfiguration
            {
                Mode = HttpRecorderMode.Replay,
                InteractionName = nameof(ItShouldWorkWithHttpRecorderContextWhenNotRecording),
            }))
            {
                var client = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("TheClient");
                Func<Task> act = async () => await client.GetAsync(ApiController.JsonUri);
                act.Should().Throw<HttpRecorderException>();
            }
        }

        [Fact]
        public void ItShouldNotAllowMultipleContexts()
        {
            using (var context = new HttpRecorderContext())
            {
                Action act = () =>
                {
                    var ctx2 = new HttpRecorderContext();
                };
                act.Should().Throw<HttpRecorderException>().WithMessage("*multiple*");
            }
        }
    }
}
