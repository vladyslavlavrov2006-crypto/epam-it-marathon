using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GroupReservation;

public sealed class SubmitGroupPreOrderRequest
{
    [Required]
    [JsonPropertyName("reservationId")]
    public string ReservationId { get; set; } = string.Empty;
}

public record GetOwnerGuestSessionIdResponse(string GuestSessionId);

public record ConsolidatedOrderItemResponse(string DishId, int Quantity);