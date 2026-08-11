using ZTMZ.PacenoteTool.AC;

namespace ZTMZ.PacenoteTool.Tests.AC;

public class ACRGameDataReaderTest
{
    [Theory]
    [InlineData("Toyota GR Yaris Rally1", "Rally1")]
    [InlineData("Skoda Fabia RS Rally2", "Rally2")]
    [InlineData("Ford Fiesta Rally3", "Rally3")]
    [InlineData("Peugeot 208 Rally4", "Rally4")]
    [InlineData("Renault Clio Rally5", "Rally5")]
    [InlineData("Ford Escort RS Cosworth H3", "H3")]
    [InlineData("Volkswagen Polo GTI R5", "R5")]
    [InlineData("Toyota Yaris WRC", "WRC")]
    [InlineData("Hyundai i20 N Rally2   ", "Rally2")]
    [InlineData("", "")]
    public void CarClass_UsesFinalVehicleNameWord(string vehicleName, string expectedCarClass)
    {
        var reader = new ACRGameDataReader();
        reader.CurrentRawGameData.StaticInfo = new ACRStaticInfo
        {
            VehicleModel = vehicleName
        };

        Assert.Equal(expectedCarClass, reader.CarClass);
    }
}
