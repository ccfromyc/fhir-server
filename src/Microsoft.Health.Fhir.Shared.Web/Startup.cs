﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.GraphQl.DataLoader;
using Microsoft.Health.Fhir.Azure;
using Microsoft.Health.Fhir.Shared.Api.Features.GraphQl;

namespace Microsoft.Health.Fhir.Web
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
#pragma warning disable CA1041 // Provide ObsoleteAttribute message
        [Obsolete]
#pragma warning restore CA1041 // Provide ObsoleteAttribute message
        public virtual void ConfigureServices(IServiceCollection services)
        {
            services.AddDevelopmentIdentityProvider(Configuration);

            _ = services
                .AddRouting()

                // Adding the GraphQL server core service
                .AddGraphQLServer()

                // Adding our scheme
                .AddDocumentFromFile("./patient.graphql")
                .AddDocumentFromFile("./types.graphql")

                // Next we add the types to our schema
                .AddQueryType(d => d.Name("Query"))
                    .AddTypeExtension<PatientQueries>()
                .BindComplexType<Address>()
                .BindComplexType<Attachment>()
                .BindComplexType<Code>()
                .BindComplexType<CodeableConcept>()
                .BindComplexType<Coding>()
                .BindComplexType<ContactPoint>()
                .BindComplexType<DataType>()
                .BindComplexType<DomainResource>()
                .BindComplexType<Element>()
                .BindComplexType<Extension>()
                .BindComplexType<FhirBoolean>()
                .BindComplexType<FhirDateTime>()
                .BindComplexType<HumanName>()
                .BindComplexType<Identifier>()
                .BindComplexType<Meta>()
                .BindComplexType<Narrative>()
                .BindComplexType<Patient>()
                .BindComplexType<Patient.LinkComponent>()
                .BindComplexType<Patient.CommunicationComponent>()
                .BindComplexType<Patient.ContactComponent>()
                .BindComplexType<Period>()
                .BindComplexType<PrimitiveType>()
                .BindComplexType<Resource>()
                .BindComplexType<ResourceReference>()

                // Adding DataLoader to our system
                .AddDataLoader<PatientByIdDataLoader>();

            var birthtime = new Extension(
                "http://hl7.org/fhir/StructureDefinition/patient-birthTime",
                new FhirDateTime(1983, 4, 23, 7, 44));

            services.AddMediatR(typeof(PatientByIdDataLoader));
            services.AddHttpContextAccessor();

            Core.Registration.IFhirServerBuilder fhirServerBuilder = services.AddFhirServer(Configuration)
                .AddAzureExportDestinationClient()
                .AddAzureExportClientInitializer(Configuration)
                .AddContainerRegistryTokenProvider()
                .AddConvertData()
                .AddMemberMatch();

            string dataStore = Configuration["DataStore"];
            if (dataStore.Equals(KnownDataStores.CosmosDb, StringComparison.OrdinalIgnoreCase))
            {
                fhirServerBuilder.AddCosmosDb();
            }
            else if (dataStore.Equals(KnownDataStores.SqlServer, StringComparison.OrdinalIgnoreCase))
            {
                fhirServerBuilder.AddSqlServer(Configuration);
            }

            /*
            The execution of IHostedServices depends on the order they are added to the dependency injection container, so we
            need to ensure that the schema is initialized before the background workers are started.
            */
            fhirServerBuilder.AddBackgroundWorkers();

            if (string.Equals(Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                services.Configure<ForwardedHeadersOptions>(options =>
                {
                    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                        ForwardedHeaders.XForwardedProto;

                    // Only loopback proxies are allowed by default.
                    // Clear that restriction because forwarders are enabled by explicit
                    // configuration.
                    options.KnownNetworks.Clear();
                    options.KnownProxies.Clear();
                });
            }

            if (bool.TryParse(Configuration["PrometheusMetrics:enabled"], out bool prometheusOn) && prometheusOn)
            {
                services.AddPrometheusMetrics(Configuration);
            }

            AddApplicationInsightsTelemetry(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(IApplicationBuilder app)
        {
            if (string.Equals(Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                app.UseForwardedHeaders();
            }

            app.UsePrometheusHttpMetrics();
            app.UseFhirServer();
            app.UseDevelopmentIdentityProviderIfConfigured();

            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGraphQL("/graphql"); // By default it is /graphql, but I can change it to /$graphql
            });
        }

        /// <summary>
        /// Adds ApplicationInsights for telemetry and logging.
        /// </summary>
        private void AddApplicationInsightsTelemetry(IServiceCollection services)
        {
            string instrumentationKey = Configuration["ApplicationInsights:InstrumentationKey"];

            if (!string.IsNullOrWhiteSpace(instrumentationKey))
            {
                services.AddApplicationInsightsTelemetry(instrumentationKey);
                services.AddSingleton<ITelemetryInitializer, CloudRoleNameTelemetryInitializer>();
                services.AddLogging(loggingBuilder => loggingBuilder.AddApplicationInsights(instrumentationKey));
            }
        }
    }
}
