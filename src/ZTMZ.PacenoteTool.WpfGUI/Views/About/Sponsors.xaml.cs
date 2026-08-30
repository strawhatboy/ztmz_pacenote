using System.Collections.Generic;
using System.Windows.Controls;
using ZTMZ.PacenoteTool.Base;

namespace ZTMZ.PacenoteTool.WpfGUI.Views.About;

partial class Sponsors : UserControl
{
    public Sponsors()
    {
        InitializeComponent();
        DataContext = this;
    }

    public string MajorSponsor => SponsorCatalog.MajorSponsor;

    public IReadOnlyList<string> SponsorNames => SponsorCatalog.SponsorNames;
}
