using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

public class SubmitGroupPreOrderHandler(IGroupReservationService groupReservationService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out string? groupReservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing groupReservationId in path parameters.", context.Logger);

        if (request.RequestContext?.Authorizer?.Claims == null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out string? userId))
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

        if (!RequestBodyHelper.TryParseAndValidate<SubmitGroupPreOrderRequest>(request, context, out var body, out var errorResponse))
            return errorResponse;

        try
        {
            var response = await groupReservationService.SubmitGroupPreOrderAsync(
                userId,
                groupReservationId,
                body.ReservationId);
            return ResponseBuilder.CreateJsonResponse(200, response);
        }
        catch (GroupReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ActionForbiddenException ex)
        {
            return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorCodeResponse(ex);
        }
        catch (Exception ex)
        {
            return ResponseBuilder.CreateErrorResponse(500, $"Internal server error: {ex.Message}", context.Logger);
        }
    }
}