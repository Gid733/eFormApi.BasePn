﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using eFormCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Database;
using Microting.eFormApi.BasePn.Database.Entities;
using Microting.eFormApi.BasePn.Helpers.WritableOptions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.eFormApi.BasePn.Models.Application;
using Microting.eFormApi.BasePn.Models.Settings.Admin;
using Microting.eFormApi.BasePn.Models.Settings.Initial;
using Microting.eFormApi.BasePn.Resources;

namespace Microting.eFormApi.BasePn.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly ILogger<SettingsService> _logger;
        private readonly IStringLocalizer<SharedResource> _localizer;
        private readonly IWritableOptions<ConnectionStrings> _connectionStrings;
        private readonly IWritableOptions<ApplicationSettings> _applicationSettings;
        private readonly IWritableOptions<LoginPageSettings> _loginPageSettings;
        private readonly IWritableOptions<HeaderSettings> _headerSettings;
        private readonly IWritableOptions<EmailSettings> _emailSettings;
        private readonly IEFormCoreService _coreHelper;

        public SettingsService(ILogger<SettingsService> logger,
            IWritableOptions<ConnectionStrings> connectionStrings,
            IWritableOptions<ApplicationSettings> applicationSettings,
            IWritableOptions<LoginPageSettings> loginPageSettings,
            IWritableOptions<HeaderSettings> headerSettings,
            IWritableOptions<EmailSettings> emailSettings,
            IEFormCoreService coreHelper, 
            IStringLocalizer<SharedResource> localizer)
        {
            _logger = logger;
            _connectionStrings = connectionStrings;
            _applicationSettings = applicationSettings;
            _loginPageSettings = loginPageSettings;
            _headerSettings = headerSettings;
            _emailSettings = emailSettings;
            _coreHelper = coreHelper;
            _localizer = localizer;
        }

        public OperationResult ConnectionStringExist()
        {
            var connectionString = _connectionStrings.Value.SdkConnection;
            if (!string.IsNullOrEmpty(connectionString))
            {
                return new OperationResult(true);
            }

            return new OperationResult(false, "Connection string does not exist");
        }

        public OperationDataResult<string> GetDefaultLocale()
        {
            try
            {
                var locale = _applicationSettings.Value.DefaultLocale;
                if (string.IsNullOrEmpty(locale))
                {
                    return new OperationDataResult<string>(true, "en-US");
                }

                return new OperationDataResult<string>(true, model: locale);
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                // We do this if any of the above fail for some reason, then we set it to default en-US
                return new OperationDataResult<string>(true, "en-US");
            }
        }

        public async Task<OperationResult> UpdateConnectionString(InitialSettingsModel initialSettingsModel)
        {
            var sdkConnectionString = initialSettingsModel.ConnectionStringSdk.Source + ";Initial Catalog="
                                                                                      + initialSettingsModel
                                                                                          .ConnectionStringSdk
                                                                                          .Catalogue + ";"
                                                                                      + initialSettingsModel
                                                                                          .ConnectionStringSdk.Auth;

            var mainConnectionString = initialSettingsModel.ConnectionStringMain.Source + ";Initial Catalog="
                                                                                        + initialSettingsModel
                                                                                            .ConnectionStringMain
                                                                                            .Catalogue + ";"
                                                                                        + initialSettingsModel
                                                                                            .ConnectionStringMain.Auth;
            if (!string.IsNullOrEmpty(_connectionStrings.Value.SdkConnection))
            {
                return new OperationResult(false, LocaleHelper.GetString("ConnectionStringAlreadyExist"));
            }

            AdminTools adminTools;
            try
            {
                adminTools = new AdminTools(sdkConnectionString);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);
                return new OperationResult(false, LocaleHelper.GetString("SDKConnectionStringIsInvalid"));
            }

            // Migrate DB
            try
            {
                var dbContextOptionsBuilder = new DbContextOptionsBuilder<BaseDbContext>();
                dbContextOptionsBuilder.UseSqlServer(mainConnectionString, b =>
                    b.MigrationsAssembly("eFormAPI.Web"));
                using (var dbContext = new BaseDbContext(dbContextOptionsBuilder.Options))
                {
                    dbContext.Database.Migrate();
                    var userStore = new UserStore<EformUser,
                        EformRole,
                        BaseDbContext,
                        int,
                        IdentityUserClaim<int>,
                        EformUserRole,
                        IdentityUserLogin<int>,
                        IdentityUserToken<int>,
                        IdentityRoleClaim<int>>(dbContext);


                    IPasswordHasher<EformUser> hasher = new PasswordHasher<EformUser>();
                    var validator = new UserValidator<EformUser>();
                    var validators = new List<UserValidator<EformUser>> {validator};
                    var userManager = new UserManager<EformUser>(userStore, null, hasher, validators, null, null, null,
                        null, null);

                    // Set-up token providers.
                    IUserTwoFactorTokenProvider<EformUser> tokenProvider = new EmailTokenProvider<EformUser>();
                    userManager.RegisterTokenProvider("Default", tokenProvider);
                    IUserTwoFactorTokenProvider<EformUser> phoneTokenProvider =
                        new PhoneNumberTokenProvider<EformUser>();
                    userManager.RegisterTokenProvider("PhoneTokenProvider", phoneTokenProvider);
                    // Roles
                    var roleStore = new RoleStore<EformRole, BaseDbContext, int>(dbContext);
                    var roleManager = new RoleManager<EformRole>(roleStore, null, null, null, null);
                    if (!await roleManager.RoleExistsAsync(EformRole.Admin))
                    {
                        await roleManager.CreateAsync(new EformRole() {Name = EformRole.Admin});
                    }

                    if (!await roleManager.RoleExistsAsync(EformRole.User))
                    {
                        await roleManager.CreateAsync(new EformRole() {Name = EformRole.User});
                    }

                    // Seed admin and demo users
                    var adminUser = new EformUser()
                    {
                        UserName = initialSettingsModel.AdminSetupModel.UserName,
                        Email = initialSettingsModel.AdminSetupModel.Email,
                        FirstName = initialSettingsModel.AdminSetupModel.FirstName,
                        LastName = initialSettingsModel.AdminSetupModel.LastName,
                        EmailConfirmed = true,
                        TwoFactorEnabled = false,
                        IsGoogleAuthenticatorEnabled = false
                    };
                    if (!userManager.Users.Any(x => x.Email.Equals(adminUser.Email)))
                    {
                        var createResult = await userManager.CreateAsync(adminUser,
                            initialSettingsModel.AdminSetupModel.Password);
                        if (!createResult.Succeeded)
                        {
                            return new OperationResult(false, LocaleHelper.GetString("Could not create the user"));
                        }
                    }

                    var user = userManager.Users.FirstOrDefault(x => x.Email.Equals(adminUser.Email));
                    if (!await userManager.IsInRoleAsync(user, EformRole.Admin))
                    {
                        await userManager.AddToRoleAsync(user, EformRole.Admin);
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);
                return new OperationResult(false, LocaleHelper.GetString("MainConnectionStringIsInvalid"));
            }

            // Setup SDK DB
            adminTools.DbSetup(initialSettingsModel.ConnectionStringSdk.Token);
            try
            {
                _connectionStrings.Update((options) =>
                {
                    options.SdkConnection = sdkConnectionString;
                    options.DefaultConnection = mainConnectionString;
                });
                _applicationSettings.Update((options) =>
                {
                    options.DefaultLocale = initialSettingsModel.GeneralAppSetupSettingsModel.DefaultLocale;
                });
            }
            catch (Exception exception)
            {
                _logger.LogError(exception.Message);
                return new OperationResult(false, LocaleHelper.GetString("CouldNotWriteConnectionString"));
            }

            return new OperationResult(true);
        }

        public OperationDataResult<LoginPageSettingsModel> GetLoginPageSettings()
        {
            try
            {
                var model = new LoginPageSettingsModel()
                {
                    ImageLink = _loginPageSettings.Value.ImageLink,
                    ImageLinkVisible = _loginPageSettings.Value.ImageLinkVisible,
                    MainText = _loginPageSettings.Value.MainText,
                    MainTextVisible = _loginPageSettings.Value.MainTextVisible,
                    SecondaryText = _loginPageSettings.Value.SecondaryText,
                    SecondaryTextVisible = _loginPageSettings.Value.SecondaryTextVisible,
                };
                return new OperationDataResult<LoginPageSettingsModel>(true, model);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return new OperationDataResult<LoginPageSettingsModel>(false,
                    LocaleHelper.GetString("CantObtainSettingsFromWebConfig"));
            }
        }

        public OperationDataResult<HeaderSettingsModel> GetPageHeaderSettings()
        {
            try
            {
                var model = new HeaderSettingsModel()
                {
                    ImageLink = _headerSettings.Value.ImageLink,
                    ImageLinkVisible = _headerSettings.Value.ImageLinkVisible,
                    MainText = _headerSettings.Value.MainText,
                    MainTextVisible = _headerSettings.Value.MainTextVisible,
                    SecondaryText = _headerSettings.Value.SecondaryText,
                    SecondaryTextVisible = _headerSettings.Value.SecondaryTextVisible,
                };
                return new OperationDataResult<HeaderSettingsModel>(true, model);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return new OperationDataResult<HeaderSettingsModel>(false, "Can't obtain settings from web.config");
            }
        }

        public OperationDataResult<AdminSettingsModel> GetAdminSettings()
        {
            try
            {
                var core = _coreHelper.GetCore();

                var model = new AdminSettingsModel()
                {
                    SMTPSettingsModel = new SMTPSettingsModel()
                    {
                        Host = _emailSettings.Value.SmtpHost,
                        Port = _emailSettings.Value.SmtpPort.ToString(),
                        Login = _emailSettings.Value.Login,
                        Password = _emailSettings.Value.Password,
                    },
                    HeaderSettingsModel = new HeaderSettingsModel()
                    {
                        ImageLink = _headerSettings.Value.ImageLink,
                        ImageLinkVisible = _headerSettings.Value.ImageLinkVisible,
                        MainText = _headerSettings.Value.MainText,
                        MainTextVisible = _headerSettings.Value.MainTextVisible,
                        SecondaryText = _headerSettings.Value.SecondaryText,
                        SecondaryTextVisible = _headerSettings.Value.SecondaryTextVisible,
                    },
                    LoginPageSettingsModel = new LoginPageSettingsModel()
                    {
                        ImageLink = _loginPageSettings.Value.ImageLink,
                        ImageLinkVisible = _loginPageSettings.Value.ImageLinkVisible,
                        MainText = _loginPageSettings.Value.MainText,
                        MainTextVisible = _loginPageSettings.Value.MainTextVisible,
                        SecondaryText = _loginPageSettings.Value.SecondaryText,
                        SecondaryTextVisible = _loginPageSettings.Value.SecondaryTextVisible,
                    },
                    SiteLink = core.GetHttpServerAddress(),
                    AssemblyVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString()
                };
                return new OperationDataResult<AdminSettingsModel>(true, model);
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return new OperationDataResult<AdminSettingsModel>(false,
                    LocaleHelper.GetString("CantObtainSettingsFromWebConfig"));
            }
        }

        public OperationResult UpdateAdminSettings(AdminSettingsModel adminSettingsModel)
        {
            try
            {
                var core = _coreHelper.GetCore();
                _emailSettings.Update((option) =>
                {
                    option.SmtpHost = adminSettingsModel.SMTPSettingsModel.Host;
                    option.SmtpPort = int.Parse(adminSettingsModel.SMTPSettingsModel.Port);
                    option.Login = adminSettingsModel.SMTPSettingsModel.Login;
                    option.Password = adminSettingsModel.SMTPSettingsModel.Password;
                });
                _headerSettings.Update((option) =>
                {
                    option.ImageLink = adminSettingsModel.HeaderSettingsModel.ImageLink;
                    option.ImageLinkVisible = adminSettingsModel.HeaderSettingsModel.ImageLinkVisible;
                    option.MainText = adminSettingsModel.HeaderSettingsModel.MainText;
                    option.MainTextVisible = adminSettingsModel.HeaderSettingsModel.MainTextVisible;
                    option.SecondaryText = adminSettingsModel.HeaderSettingsModel.SecondaryText;
                    option.SecondaryTextVisible = adminSettingsModel.HeaderSettingsModel.SecondaryTextVisible;
                });
                _loginPageSettings.Update((option) =>
                {
                    option.ImageLink = adminSettingsModel.LoginPageSettingsModel.ImageLink;
                    option.ImageLinkVisible = adminSettingsModel.LoginPageSettingsModel.ImageLinkVisible;
                    option.MainText = adminSettingsModel.LoginPageSettingsModel.MainText;
                    option.MainTextVisible = adminSettingsModel.LoginPageSettingsModel.MainTextVisible;
                    option.SecondaryText = adminSettingsModel.LoginPageSettingsModel.SecondaryText;
                    option.SecondaryTextVisible = adminSettingsModel.LoginPageSettingsModel.SecondaryTextVisible;
                });
                core.SetHttpServerAddress(adminSettingsModel.SiteLink);
                return new OperationResult(true, LocaleHelper.GetString("SettingsUpdatedSuccessfully"));
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return new OperationResult(false, LocaleHelper.GetString("CantUpdateSettingsInWebConfig"));
            }
        }

        #region ResetSettingsSection

        public OperationResult ResetLoginPageSettings()
        {
            try
            {
                _loginPageSettings.Update((option) =>
                {
                    option.ImageLink = "";
                    option.ImageLinkVisible = true;
                    option.MainText = "Microting eForm";
                    option.MainTextVisible = true;
                    option.SecondaryText = "No more paper-forms and back-office data entry";
                    option.SecondaryTextVisible = true;
                });
                return new OperationResult(true, "Login page settings have been reseted successfully");
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return new OperationResult(false, "Can't update settings in web.config");
            }
        }

        public OperationResult ResetPageHeaderSettings()
        {
            try
            {
                _headerSettings.Update((option) =>
                {
                    option.ImageLink = "";
                    option.ImageLinkVisible = true;
                    option.MainText = "Microting eForm";
                    option.MainTextVisible = true;
                    option.SecondaryText = "No more paper-forms and back-office data entry";
                    option.SecondaryTextVisible = true;
                });
                return new OperationResult(true, "Header settings have been reseted successfully");
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
                return new OperationResult(false, "Can't update settings in web.config");
            }
        }

        #endregion
    }
}