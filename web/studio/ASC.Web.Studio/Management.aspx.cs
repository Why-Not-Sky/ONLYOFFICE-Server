/*
 * 
 * (c) Copyright Ascensio System SIA 2010-2014
 * 
 * This program is a free software product.
 * You can redistribute it and/or modify it under the terms of the GNU Affero General Public License
 * (AGPL) version 3 as published by the Free Software Foundation. 
 * In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended to the effect 
 * that Ascensio System SIA expressly excludes the warranty of non-infringement of any third-party rights.
 * 
 * This program is distributed WITHOUT ANY WARRANTY; 
 * without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
 * For details, see the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
 * 
 * You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
 * 
 * The interactive user interfaces in modified source and object code versions of the Program 
 * must display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
 * 
 * Pursuant to Section 7(b) of the License you must retain the original Product logo when distributing the program. 
 * Pursuant to Section 7(e) we decline to grant you any rights under trademark law for use of our trademarks.
 * 
 * All the Product's GUI elements, including illustrations and icon sets, as well as technical 
 * writing content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0 International. 
 * See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode
 * 
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using ASC.Core;
using ASC.Web.Core.Files;
using ASC.Web.Core.Utility.Settings;
using ASC.Web.Studio.Core;
using ASC.Web.Studio.UserControls.Common.HelpCenter;
using ASC.Web.Studio.UserControls.Common.Support;
using ASC.Web.Studio.UserControls.Common.VideoGuides;
using ASC.Web.Studio.UserControls.Common.UserForum;
using ASC.Web.Studio.Utility;
using Resources;
using System.Configuration;
using ASC.Core.Common.Contracts;

namespace ASC.Web.Studio
{
    public partial class Management : MainPage
    {
        private const ManagementType DefaultModule = ManagementType.Customization;

        protected static readonly Lazy<Dictionary<ManagementType, ManagementControlAttribute[]>> ManagementModules =
            new Lazy<Dictionary<ManagementType, ManagementControlAttribute[]>>(LoadModules, LazyThreadSafetyMode.PublicationOnly);

        private readonly bool auditTrailEnabled = true;

        public TenantAccessSettings TenantAccess { get; private set; }

        protected ManagementType CurrentModule
        {
            get
            {
                ManagementType currentModule;
                if (!Enum.TryParse(Request["type"], out currentModule))
                {
                    currentModule = DefaultModule;
                }
                if (!ManagementModules.Value.ContainsKey(currentModule) && currentModule != ManagementType.HelpCenter)
                {
                    currentModule = DefaultModule;
                }
                if (TenantAccess.Anyone && currentModule == ManagementType.Backup)
                {
                    currentModule = DefaultModule;
                }

                return currentModule;
            }
        }

        protected override void OnPreInit(EventArgs e)
        {
            base.OnPreInit(e);
            if (CoreContext.Configuration.Personal)
            {
                Context.Response.Redirect(FilesLinkUtility.FilesBaseAbsolutePath);
            }

            if (!SecurityContext.CheckPermissions(SecutiryConstants.EditPortalSettings))
            {
                Response.Redirect(CommonLinkUtility.GetDefault());
            }

            TenantAccess = SettingsManager.Instance.LoadSettings<TenantAccessSettings>(TenantProvider.CurrentTenantID);
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            var help = (HelpCenter)LoadControl(HelpCenter.Location);
            help.IsSideBar = true;
            HelpHolder.Controls.Add(help);
            SupportHolder.Controls.Add(LoadControl(Support.Location));
            VideoGuides.Controls.Add(LoadControl(VideoGuidesControl.Location));
            UserForumHolder.Controls.Add(LoadControl(UserForum.Location));

            Page.Title = GetPageTitle(CurrentModule);

            if (CurrentModule == ManagementType.HelpCenter)
            {
                SettingsContainer.Controls.Add(LoadControl(HelpCenter.Location));
                return;
            }

            foreach (var control in ManagementModules.Value[CurrentModule].Where(IsControlVisibleForCurrentUser))
            {
                SettingsContainer.Controls.Add(LoadControl(control.Location));
            }
        }

        protected string GetPageTitle(ManagementType module)
        {
            return HeaderStringHelper.GetPageTitle(GetNavigationTitle(module));
        }

        protected List<ManagementType> GetNavigationList()
        {
            return ManagementModules.Value
                                    .Where(keyValue => keyValue.Value.Any(IsControlVisibleForCurrentUser))
                                    .Select(keyValue => keyValue.Key)
                                    .ToList();
        }

        protected string GetNavigationTitle(ManagementType module)
        {
            switch (module)
            {
                case ManagementType.Statistic:
                    return Resource.StatisticsTitle;
                case ManagementType.AuditTrail:
                    return AuditResource.AuditTrailNav;
                case ManagementType.LoginHistory:
                    return AuditResource.LoginHistoryNav;
                case ManagementType.PortalSecurity:
                    return Resource.PortalSecurity;
                case ManagementType.SmtpSettings:
                    return Resource.SmtpSettings;
                case ManagementType.DeletionPortal:
                    return Resource.DeactivationDeletionPortal;
                default:
                    return Resource.ResourceManager.GetString(module.ToString()) ?? module.ToString();
            }
        }

        protected class TransferRegionWithName : TransferRegion
        {
            public string FullName { get; set; }
        }

        private List<TransferRegionWithName> _transferRegions;

        protected List<TransferRegionWithName> TransferRegions
        {
            get { return _transferRegions ?? (_transferRegions = GetRegions()); }
        }

        private static List<TransferRegionWithName> GetRegions()
        {
            try
            {
                using (var backupClient = new BackupServiceClient())
                {
                    return backupClient.GetTransferRegions()
                                       .Select(x => new TransferRegionWithName
                                           {
                                               Name = x.Name,
                                               IsCurrentRegion = x.IsCurrentRegion,
                                               BaseDomain = x.BaseDomain,
                                               FullName = TransferResourceHelper.GetRegionDescription(x.Name)
                                           })
                                       .ToList();
                }
            }
            catch
            {
                return new List<TransferRegionWithName>();
            }
        }

        protected bool DisplayModule(ManagementType module)
        {
            switch (module)
            {
                case ManagementType.Migration:
                    return (ConfigurationManager.AppSettings["web.migration.status"] == "true") && TransferRegions.Count > 1;

                    case ManagementType.Backup:
                    return !TenantAccess.Anyone && SetupInfo.IsVisibleSettings(module.ToString());

                default:
                    return SetupInfo.IsVisibleSettings(module.ToString());
            }
        }

        protected bool DisplayModuleList(CategorySettings category)
        {
            return GetNavigationList().Intersect(category.Modules).Any();
        }

        protected class CategorySettings
        {
            public String Title;
            public List<ManagementType> Modules;
            public ManagementType ModuleUrl;
            public String ClassName;
        }

        protected List<CategorySettings> Category
        {
            get
            {
                var securityCategorySettings = new CategorySettings
                    {
                        Title = Resource.ManagementCategorySecurity,
                        Modules = new List<ManagementType>
                            {
                                ManagementType.PortalSecurity,
                                ManagementType.AccessRights
                            },
                        ClassName = "security"
                    };

                if (auditTrailEnabled)
                {
                    securityCategorySettings.Modules.AddRange(new List<ManagementType> {ManagementType.LoginHistory, ManagementType.AuditTrail});
                }

                var list = new List<CategorySettings>
                    {
                        new CategorySettings
                            {
                                Title = Resource.ManagementCategoryCommon,
                                Modules = new List<ManagementType>
                                    {
                                        ManagementType.Customization,
                                        ManagementType.ProductsAndInstruments
                                    },
                                ClassName = "general"
                            },
                        securityCategorySettings,
                        new CategorySettings
                            {
                                Title = Resource.DataManagement,
                                Modules = new List<ManagementType>
                                    {
                                        ManagementType.Migration,
                                        ManagementType.Backup,
                                        ManagementType.DeletionPortal
                                    },
                                ClassName = "backup"
                            },
                        new CategorySettings
                            {
                                Title = Resource.ManagementCategoryIntegration,
                                Modules = new List<ManagementType>
                                    {
                                        ManagementType.LdapSettings,
                                        ManagementType.ThirdPartyAuthorization,
                                        ManagementType.SmtpSettings
                                    },
                                ClassName = "productsandinstruments"
                            },
                        new CategorySettings
                            {
                                Title = Resource.ManagementCategoryStatistic,
                                ModuleUrl = ManagementType.Statistic,
                                ClassName = "statistic"
                            },
                        new CategorySettings
                            {
                                Title = Resource.Monitoring,
                                ModuleUrl = ManagementType.Monitoring,
                                ClassName = "monitoring"
                            }
                    };
                return list;
            }
        }

        protected string GetNavigationUrl(ManagementType module)
        {
            return CommonLinkUtility.GetAdministration(module);
        }

        private static Dictionary<ManagementType, ManagementControlAttribute[]> LoadModules()
        {
            return Assembly.GetExecutingAssembly()
                           .GetTypes()
                           .Select(type => IsControlVisible(type) ? type.GetCustomAttribute<ManagementControlAttribute>() : null)
                           .Where(control => control != null && IsModuleVisible(control.Module))
                           .GroupBy(control => control.Module)
                           .OrderBy(group => (int)group.Key)
                           .ToDictionary(group => group.Key, group => group.OrderBy(control => control.SortOrder)
                                                                           .ToArray());
        }

        private static bool IsControlVisibleForCurrentUser(ManagementControlAttribute control)
        {
            return control.IsVisibleForAdministrator || (SecurityContext.CurrentAccount.ID == CoreContext.TenantManager.GetCurrentTenant().OwnerId);
        }

        private static bool IsControlVisible(Type type)
        {
            return SetupInfo.IsVisibleSettings(type.Name);
        }

        private static bool IsModuleVisible(ManagementType type)
        {
            return SetupInfo.IsVisibleSettings(type.ToString());
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ManagementControlAttribute : Attribute
    {
        private readonly ManagementType _module;

        public ManagementType Module
        {
            get { return _module; }
        }

        private readonly string _location;

        public string Location
        {
            get { return _location; }
        }

        public int SortOrder { get; set; }

        public bool IsVisibleForAdministrator { get; set; }

        public ManagementControlAttribute(ManagementType module, string location)
        {
            _module = module;
            _location = location;
            IsVisibleForAdministrator = true;
        }
    }
}