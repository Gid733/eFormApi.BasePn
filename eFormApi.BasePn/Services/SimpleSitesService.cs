﻿using System;
using System.Collections.Generic;
using eFormShared;
using Microsoft.Extensions.Localization;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Models;
using Microting.eFormApi.BasePn.Resources;

namespace Microting.eFormApi.BasePn.Services
{
    public class SimpleSitesService : ISimpleSitesService
    {
        private readonly IEFormCoreService _coreHelper;
        private readonly IStringLocalizer<SharedResource> _localizer;

        public SimpleSitesService(IEFormCoreService coreHelper, 
            IStringLocalizer<SharedResource> localizer)
        {
            _coreHelper = coreHelper;
            _localizer = localizer;
        }

        public OperationDataResult<List<Site_Dto>> Index()
        {
            var core = _coreHelper.GetCore();
            var siteDto = core.SiteReadAll(false);
            return new OperationDataResult<List<Site_Dto>>(true, siteDto);
        }

        public OperationResult Create(SimpleSiteModel simpleSiteModel)
        {
            var core = _coreHelper.GetCore();
            var siteName = simpleSiteModel.UserFirstName + " " + simpleSiteModel.UserLastName;

            try
            {
                var siteDto = core.SiteCreate(siteName, simpleSiteModel.UserFirstName, simpleSiteModel.UserLastName,
                    null);

                return siteDto != null
                    ? new OperationResult(true,
                        LocaleHelper.GetString("DeviceUserParamCreatedSuccessfully", siteDto.SiteName))
                    : new OperationResult(false, LocaleHelper.GetString("DeviceUserCouldNotBeCreated"));
            }
            catch (Exception ex)
            {
                try
                {
                    if (ex.InnerException.Message == "The remote server returned an error: (402) Payment Required.")
                    {
                        return new OperationResult(false, LocaleHelper.GetString("YouNeedToBuyMoreLicenses"));
                    }
                    else
                    {
                        return new OperationResult(false, LocaleHelper.GetString("DeviceUserCouldNotBeCreated"));
                    }
                }
                catch
                {
                    return new OperationResult(false, LocaleHelper.GetString("DeviceUserCouldNotBeCreated"));
                }
            }
        }

        public OperationDataResult<Site_Dto> Edit(int id)
        {
            var core = _coreHelper.GetCore();
            var siteDto = core.SiteRead(id);

            return siteDto != null
                ? new OperationDataResult<Site_Dto>(true, siteDto)
                : new OperationDataResult<Site_Dto>(false,
                    LocaleHelper.GetString("DeviceUserParamCouldNotBeEdited", id));
        }

        public OperationResult Update(SimpleSiteModel simpleSiteModel)
        {
            try
            {
                var core = _coreHelper.GetCore();
                var siteDto = core.SiteRead(simpleSiteModel.Id);
                if (siteDto.WorkerUid != null)
                {
                    var workerDto = core.Advanced_WorkerRead((int) siteDto.WorkerUid);
                    if (workerDto != null)
                    {
                        var fullName = simpleSiteModel.UserFirstName + " " + simpleSiteModel.UserLastName;
                        var isUpdated = core.SiteUpdate(simpleSiteModel.Id, fullName, simpleSiteModel.UserFirstName,
                            simpleSiteModel.UserLastName, workerDto.Email);

                        return isUpdated
                            ? new OperationResult(true, LocaleHelper.GetString("DeviceUserUpdatedSuccessfully"))
                            : new OperationResult(false,
                                LocaleHelper.GetString("DeviceUserParamCouldNotBeUpdated", simpleSiteModel.Id));
                    }

                    return new OperationResult(false, LocaleHelper.GetString("DeviceUserCouldNotBeObtained"));
                }

                return new OperationResult(false, LocaleHelper.GetString("DeviceUserNotFound"));
            }
            catch (Exception)
            {
                return new OperationResult(false, LocaleHelper.GetString("DeviceUserCouldNotBeUpdated"));
            }
        }

        public OperationResult Delete(int id)
        {
            try
            {
                var core = _coreHelper.GetCore();
                var siteNameDto = core.Advanced_SiteItemRead(id);

                return core.SiteDelete(siteNameDto.SiteUId)
                    ? new OperationResult(true,
                        LocaleHelper.GetString("DeviceUserParamDeletedSuccessfully", siteNameDto.SiteName))
                    : new OperationResult(false,
                        LocaleHelper.GetString("DeviceUserParamCouldNotBeDeleted", siteNameDto.SiteName));
            }
            catch (Exception)
            {
                return new OperationResult(false, LocaleHelper.GetString("DeviceUserParamCouldNotBeDeleted", id));
            }
        }
    }
}