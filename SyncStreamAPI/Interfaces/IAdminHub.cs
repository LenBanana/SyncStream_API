using System.Collections.Generic;
using System.Threading.Tasks;
using SyncStreamAPI.Models;
using SyncStreamAPI.Models.ServerHealth;
using SyncStreamAPI.Wire.Users;

namespace SyncStreamAPI.Interfaces;

public partial interface IServerHub
{
    Task getPrivilegeInfo(List<PrivilegeInfo> info);
    Task userlogin(SelfProfileDto user);

    Task getusers(List<UserSummaryDto> users);
    Task serverHealth(ServerHealthDto healthDto);
}