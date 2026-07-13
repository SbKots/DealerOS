namespace DealerOS.Modules.IdentityAccess;

public static class Permissions
{
    public const string VehiclesRead = "vehicles.read";
    public const string VehiclesCreate = "vehicles.create";
    public const string VehiclesAccept = "vehicles.accept";

    public static readonly string[] VehicleOperator = [VehiclesRead, VehiclesCreate, VehiclesAccept];
}
