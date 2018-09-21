﻿using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Models.Common;
using Microting.eFormApi.BasePn.Models.User;

namespace Microting.eFormApi.BasePn.Abstractions
{
    public interface IAdminService
    {
        Task<OperationResult> CreateUser(UserRegisterModel userRegisterModel);
        Task<OperationResult> DeleteUser(int userId);
        OperationResult DisableTwoFactorAuthForce();
        OperationResult EnableTwoFactorAuthForce();
        OperationDataResult<UserInfoModelList> GetAllUsers(PaginationModel paginationModel);
        Task<OperationDataResult<UserRegisterModel>> GetUser(int userId);
        Task<OperationResult> UpdateUser(UserRegisterModel userRegisterModel);
    }
}