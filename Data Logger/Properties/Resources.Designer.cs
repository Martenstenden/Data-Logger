//------------------------------------------------------------------------------



//



//------------------------------------------------------------------------------

using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace Data_Logger.Properties
{
    [GeneratedCode("System.Resources.Tools.StronglyTypedResourceBuilder",
        "4.0.0.0")]
    [DebuggerNonUserCode()]
    [CompilerGenerated()]
    internal class Resources
    {
        private static ResourceManager resourceMan;

        private static CultureInfo resourceCulture;

        [SuppressMessage("Microsoft.Performance",
            "CA1811:AvoidUncalledPrivateCode")]
        internal Resources()
        {
        }

        [EditorBrowsable(EditorBrowsableState
            .Advanced)]
        internal static ResourceManager ResourceManager
        {
            get
            {
                if ((resourceMan == null))
                {
                    ResourceManager temp =
                        new ResourceManager("Data_Logger.Properties.Resources",
                            typeof(Resources).Assembly);
                    resourceMan = temp;
                }

                return resourceMan;
            }
        }

        [EditorBrowsable(EditorBrowsableState
            .Advanced)]
        internal static CultureInfo Culture
        {
            get { return resourceCulture; }
            set { resourceCulture = value; }
        }
    }
}