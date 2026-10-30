using System.Collections.Generic;

namespace APIHandler.Models.GuestSessions;

public record GroupReservationSessionsResponse(
    IReadOnlyList<GroupReservationParticipantResponse> Participants,
    decimal Total);

public record GroupReservationParticipantResponse(
    string Id,
    string Type,
    string Name,
    IReadOnlyList<GroupReservationSessionItemResponse> Items,
    decimal Total,
    bool IsSubmitted);

public record GroupReservationSessionItemResponse(
    GroupReservationSessionDishResponse Dish,
    int Quantity);

public record GroupReservationSessionDishResponse(
    string Id,
    string Name,
    decimal Price);