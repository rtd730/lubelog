using CarCareTracker.Models;

namespace CarCareTracker.External.Interfaces
{
    public interface IFilterDefinitionDataAccess
    {
        List<FilterDefinition> GetFiltersByVehicleId(int vehicleId);
        FilterDefinition GetFilterById(int filterId);
        bool SaveFilter(FilterDefinition filter);
        bool DeleteFilterById(int filterId);
    }
}
