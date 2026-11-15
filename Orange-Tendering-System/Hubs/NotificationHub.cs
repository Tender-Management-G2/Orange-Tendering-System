using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Orange_Tendering_System.Hubs;

// Live notification channel. SignalR addresses clients by the Identity user id (NameIdentifier claim), so a message
// is only ever delivered to the connections of the user the Notification row belongs to.
[Authorize]
public sealed class NotificationHub : Hub { }
