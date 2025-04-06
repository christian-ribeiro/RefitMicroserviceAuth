﻿using Microsoft.AspNetCore.Http;
using RefitExample.ApiClient.Interface.Service.Microservice.Authentication;
using RefitExample.Arguments.Argument.Refit.Microservice.Endpoint.Authentication;
using RefitExample.Arguments.Argument.Session;
using RefitExample.Arguments.Enum.Microservice;
using System.Net;
using System.Net.Http.Headers;

namespace RefitExample.ApiClient.Refit.Microservice.Handler;

public class MicroserviceHandler(IAuthenticationService authenticationService, IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    public const string AuthorizationHeader = "Authorization";
    public const string GuidSessionDataRequest = "GuidSessionDataRequest";
    public const string RefitClientHeader = "X-Refit-Client";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await SendWithAuthRetryAsync(request, false, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendWithAuthRetryAsync(HttpRequestMessage request, bool isRetry, CancellationToken cancellationToken)
    {
        var guidSessionDataRequest = GetGuidSessionDataRequest();
        long loggedEnterpriseId = SessionData.GetLoggedEnterprise(guidSessionDataRequest) ?? 0;
        EnumMicroservice microservice = GetMicroservice(request);

        var authentication = MicroserviceAuthCache.TryGetValidAuth(loggedEnterpriseId, microservice);
        if (authentication == null && !isRetry)
        {
            await Authenticate(loggedEnterpriseId, microservice);
            return await SendWithAuthRetryAsync(request, true, cancellationToken);
        }

        UpdateAuthorizationHeader(request, authentication?.Token);

        var response = await base.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode && response.StatusCode == HttpStatusCode.Unauthorized && !isRetry)
        {
            await Authenticate(loggedEnterpriseId, microservice);
            return await SendWithAuthRetryAsync(request, true, cancellationToken);
        }

        return response;
    }

    private static void UpdateAuthorizationHeader(HttpRequestMessage request, string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private async Task Authenticate(long loggedEntepriseId, EnumMicroservice microservice)
    {
        if (loggedEntepriseId == 0)
            return;

        var authenticate = await authenticationService.Login(new InputAuthenticateUser("eve.holt@reqres.in", "cityslicka"));
        MicroserviceAuthCache.AddOrUpdateAuth(loggedEntepriseId, microservice, new MicroserviceAuthentication(authenticate.Token));
    }

    private Guid GetGuidSessionDataRequest()
    {
        if (httpContextAccessor.HttpContext.Request.Headers.TryGetValue(GuidSessionDataRequest, out var values) && Guid.TryParse(values.FirstOrDefault(), out var guidSessionDataRequest))
        {
            return guidSessionDataRequest;
        }

        return Guid.Empty;
    }

    private static EnumMicroservice GetMicroservice(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues(RefitClientHeader, out var values) && Enum.TryParse<EnumMicroservice>(values.FirstOrDefault(), out var enumValue))
        {
            return enumValue;
        }

        return EnumMicroservice.None;
    }
}