using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ZTMZ.PacenoteTool.Base.UI {
    /// <summary>
    /// SeparateWindowForOverlay.xaml 的交互逻辑
    /// </summary>
    public partial class SeparateWindowForOverlay : Window {
        public SeparateWindowForOverlay() {
            InitializeComponent();

            this.MouseDown += (o, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); };
        }
    }
}
