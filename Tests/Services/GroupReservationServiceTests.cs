using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupReservationServiceTests
{
    private readonly Mock<IGroupReservationRepository> _groupReservationRepositoryMock = new();
    private readonly Mock<ITableRepository> _tableRepositoryMock = new();
    private readonly Mock<IReservationRepository> _reservationRepositoryMock = new();
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<ILocationService> _locationServiceMock = new();

    private readonly GroupReservationService _service;

    public GroupReservationServiceTests()
    {
        _service = new GroupReservationService(
            _groupReservationRepositoryMock.Object,
            _tableRepositoryMock.Object,
            _reservationRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _userServiceMock.Object,
            _locationServiceMock.Object,
            _orderRepositoryMock.Object);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_CreatesAndReturnsOwnerSession(
        GroupReservation groupReservation,
        Reservation reservation)
    {
        const string userId = "owner-id";
        reservation.CustomerId = userId;
        groupReservation.ReservationId = reservation.Id;
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _groupReservationRepositoryMock
            .Setup(repository => repository.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([]);

        GuestSession? savedSession = null;
        _guestSessionRepositoryMock
            .Setup(repository => repository.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(session => savedSession = session);

        GetOwnerGuestSessionIdResponse response = await _service
            .GetOwnerGuestSessionIdAsync(userId, reservation.Id);

        Assert.NotNull(savedSession);
        Assert.Equal(response.GuestSessionId, savedSession.Id);
        Assert.True(savedSession.IsOwnerSession);
        Assert.Equal(userId, savedSession.OwnerUserId);
        Assert.Equal(groupReservation.GroupReservationId, savedSession.GroupReservationId);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ConsolidatesOnlySubmittedSessions(
        GroupReservation groupReservation,
        Reservation reservation,
        Order order,
        GuestSession submittedSession,
        GuestSession unsubmittedSession)
    {
        const string userId = "owner-id";
        groupReservation.GroupReservationId = "group-id";
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;
        order.ReservationId = reservation.Id;
        order.Dishes = [];
        submittedSession.GroupReservationId = groupReservation.GroupReservationId;
        submittedSession.IsSubmitted = true;
        submittedSession.Dishes =
        [
            new OrderItem { DishId = "dish-a", Quantity = 2, State = OrderItemState.Pending },
            new OrderItem { DishId = "dish-a", Quantity = 3, State = OrderItemState.Pending },
            new OrderItem { DishId = "dish-b", Quantity = 1, State = OrderItemState.Pending }
        ];
        unsubmittedSession.GroupReservationId = groupReservation.GroupReservationId;
        unsubmittedSession.IsSubmitted = false;
        unsubmittedSession.Dishes =
        [new OrderItem { DishId = "dish-a", Quantity = 20, State = OrderItemState.Pending }];

        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([submittedSession, unsubmittedSession]);

        var response = await _service.SubmitGroupPreOrderAsync(
            userId,
            groupReservation.GroupReservationId,
            reservation.Id);

        Assert.Equal(2, response.Count);
        Assert.Equal(5, response.Single(item => item.DishId == "dish-a").Quantity);
        Assert.Equal(1, response.Single(item => item.DishId == "dish-b").Quantity);
        Assert.Equal(2, order.Dishes.Count);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(GroupReservationStatus.Submitted, groupReservation.Status);
        _orderRepositoryMock.Verify(repository => repository.SaveOrderAsync(order), Times.Once);
        _groupReservationRepositoryMock.Verify(repository => repository.SaveGroupReservationAsync(groupReservation), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NoSubmittedSessions_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation,
        Order order,
        GuestSession guestSession)
    {
        const string userId = "owner-id";
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;
        order.ReservationId = reservation.Id;
        guestSession.IsSubmitted = false;

        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([guestSession]);

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync(
            userId,
            groupReservation.GroupReservationId,
            reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SubmittedSessionsWithoutDishes_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation,
        Order order,
        GuestSession guestSession)
    {
        const string userId = "owner-id";
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;
        order.ReservationId = reservation.Id;
        guestSession.IsSubmitted = true;
        guestSession.Dishes = [];

        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetAllByGroupReservationIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync([guestSession]);

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync(
            userId,
            groupReservation.GroupReservationId,
            reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_MissingOrder_ThrowsValidationException(
        GroupReservation groupReservation,
        Reservation reservation)
    {
        const string userId = "owner-id";
        groupReservation.ReservationId = reservation.Id;
        reservation.CustomerId = userId;

        _groupReservationRepositoryMock
            .Setup(repository => repository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
        _orderRepositoryMock
            .Setup(repository => repository.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync(
            userId,
            groupReservation.GroupReservationId,
            reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_NonOwner_ThrowsForbidden(
        GroupReservation groupReservation,
        Reservation reservation)
    {
        reservation.CustomerId = "actual-owner";
        groupReservation.ReservationId = reservation.Id;
        _reservationRepositoryMock
            .Setup(repository => repository.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service
            .GetOwnerGuestSessionIdAsync("different-user", reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_ValidInput_ReturnsGroupReservationInfo(
        GroupReservation groupReservation,
        Table table,
        Reservation reservation,
        Location location,
        string expectedUserName)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.TableId = table.Id;
        table.LocationId = location.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(0);

        _tableRepositoryMock
            .Setup(r => r.GetTableAsync(table.Id))
            .ReturnsAsync(table);

        _locationServiceMock
            .Setup(s => s.GetLocationByIdAsync(table.LocationId))
            .ReturnsAsync(location);

        _userServiceMock
            .Setup(s => s.GetUserName(reservation.CustomerId!))
            .ReturnsAsync(expectedUserName);

        var result = await _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken);

        Assert.Equal(groupReservation.GroupReservationId, result.GroupReservationId);
        Assert.Equal(table.LocationId, result.LocationId);
        Assert.Equal(reservation.GuestCount, result.MaxGuestCount);
        Assert.Equal(reservation.StartTime, result.StartTime);
        Assert.Equal(reservation.EndTime, result.EndTime);
        Assert.Equal(table.TableNumber, result.TableNumber);
        Assert.Equal("Green & Tasty", result.RestaurantName);
        Assert.Equal(location.Address, result.LocationAddress);
        Assert.Equal(expectedUserName, result.InviterName);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_InvalidToken_ThrowsNotFound(string token)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(token))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetGroupReservationByTokenAsync(token));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed)]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    public async Task GetGroupReservationByTokenAsync_GroupReservationIsNotOpen_ThrowsException(
        GroupReservationStatus status,
        GroupReservation groupReservation)
    {
        groupReservation.Status = status;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync<GroupReservationExpiredException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationByTokenAsync_WhenGuestCountIsMax_ThrowsGuestLimitExceeded(GroupReservation groupReservation, Reservation reservation)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = reservation.Id;

        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByTokenAsync(groupReservation.ReservationToken))
            .ReturnsAsync(groupReservation);

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _guestSessionRepositoryMock
            .Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId))
            .ReturnsAsync(reservation.GuestCount);

        await Assert.ThrowsAsync<GuestLimitExceededException>(() => _service.GetGroupReservationByTokenAsync(groupReservation.ReservationToken));
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_ValidInput_UpdatesGroupReservationStatus(GroupReservation groupReservation)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(groupReservation.ReservationId))
            .ReturnsAsync(groupReservation);

        GroupReservation? savedGroupReservation = null;
        _groupReservationRepositoryMock
            .Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback<GroupReservation>(r => savedGroupReservation = r);

        await _service.CloseGroupReservationAsync(groupReservation.ReservationId);

        Assert.NotNull(savedGroupReservation);
        Assert.Equal(GroupReservationStatus.Closed, savedGroupReservation.Status);
    }

    [Theory]
    [AutoData]
    public async Task CloseGroupReservationAsync_GroupReservationNotFound_DoesNotThrowException(string reservationId)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservationId))
            .ReturnsAsync((GroupReservation?)null);

        await _service.CloseGroupReservationAsync(reservationId);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_ValidInput_ReturnsToken(Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        GetGroupInviteTokenResponse result = await _service.GetGroupReservationInviteTokenAsync(reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_InvalidReservationId_ThrowsNotFound(string reservationId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetGroupReservationInviteTokenAsync(reservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupReservationInviteTokenAsync_OneGuest_ThrowsValidationException(Reservation reservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(ReservationStatus.Completed)]
    [InlineAutoData(ReservationStatus.Cancelled)]
    [InlineAutoData(ReservationStatus.InProgress)]
    public async Task GetGroupReservationInviteTokenAsync_StatusIsNotReserved_ThrowsValidationException(ReservationStatus reservationStatus, Reservation reservation)
    {
        reservation.Status = reservationStatus;
        reservation.GuestCount = 1;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ValidationException>(() => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    public async Task GetGroupReservationInviteTokenAsync_GroupStatusIsNotOpen_ThrowsException(GroupReservationStatus status, Type type, Reservation reservation, GroupReservation groupReservation)
    {
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 3;
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = status;

        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);

        await Assert.ThrowsAsync(type, () => _service.GetGroupReservationInviteTokenAsync(reservation.Id));
    }
}
