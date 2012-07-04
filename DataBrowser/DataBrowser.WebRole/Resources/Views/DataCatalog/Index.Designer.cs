﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.225
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace DataCatalogResources {
    using System;
    
    
    /// <summary>
    ///   A strongly-typed resource class, for looking up localized strings, etc.
    /// </summary>
    // This class was auto-generated by the StronglyTypedResourceBuilder
    // class via a tool like ResGen or Visual Studio.
    // To add or remove a member, edit your .ResX file then rerun ResGen
    // with the /str option, or rebuild your VS project.
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("System.Resources.Tools.StronglyTypedResourceBuilder", "4.0.0.0")]
    [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    public class Index {
        
        private static global::System.Resources.ResourceManager resourceMan;
        
        private static global::System.Globalization.CultureInfo resourceCulture;
        
        [global::System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        internal Index() {
        }
        
        /// <summary>
        ///   Returns the cached ResourceManager instance used by this class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Resources.ResourceManager ResourceManager {
            get {
                if (object.ReferenceEquals(resourceMan, null)) {
                    global::System.Resources.ResourceManager temp = new global::System.Resources.ResourceManager("Ogdi.InteractiveSdk.Mvc.Resources.Views.DataCatalog.Index", typeof(Index).Assembly);
                    resourceMan = temp;
                }
                return resourceMan;
            }
        }
        
        /// <summary>
        ///   Overrides the current thread's CurrentUICulture property for all
        ///   resource lookups using this strongly typed resource class.
        /// </summary>
        [global::System.ComponentModel.EditorBrowsableAttribute(global::System.ComponentModel.EditorBrowsableState.Advanced)]
        public static global::System.Globalization.CultureInfo Culture {
            get {
                return resourceCulture;
            }
            set {
                resourceCulture = value;
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Data Catalog.
        /// </summary>
        public static string DataCatalog {
            get {
                return ResourceManager.GetString("DataCatalog", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to Data in the Open Government Data Initiative is organized
        ///into containers, each of which represents data from a specific jurisdiction or organization.
        ///Within a data set, containers are organized into categories..
        /// </summary>
        public static string DataInTheOpenGovernmentDataInitiativeIsOrganized {
            get {
                return ResourceManager.GetString("DataInTheOpenGovernmentDataInitiativeIsOrganized", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to LEGAL DISCLAIMER.
        /// </summary>
        public static string LegalDisclaimer {
            get {
                return ResourceManager.GetString("LegalDisclaimer", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to To discover data sets, first select a container. The grid will show all of the data
        ///sets in that container. If you would like to only show data sets in a particular
        ///category, click one of the categories along the left side of this page..
        /// </summary>
        public static string ToDiscoverDataSets {
            get {
                return ResourceManager.GetString("ToDiscoverDataSets", resourceCulture);
            }
        }
        
        /// <summary>
        ///   Looks up a localized string similar to To query data within a data set, click the name of the data set in the grid. The
        ///grid also shows the source of the data set and a brief description. To view extended
        ///information about a particular data set (if available), click the &amp;amp;ldquo;More
        ///Information&amp;amp;rdquo; link under the data set&apos;s description..
        /// </summary>
        public static string ToQueryDataWithinADataSet {
            get {
                return ResourceManager.GetString("ToQueryDataWithinADataSet", resourceCulture);
            }
        }
    }
}