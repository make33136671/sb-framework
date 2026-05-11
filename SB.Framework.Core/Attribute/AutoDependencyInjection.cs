using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SB.Framework.Core.Attribute
{
    public class AutoDependencyInjectionAttribute : System.Attribute
    {
        public AutoDependencyInjectionAttribute() { }
        public bool AutoDependencyInjection { get; set; } = true;
    }
}
