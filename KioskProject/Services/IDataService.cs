using KioskProject.Models;

namespace KioskProject.Services;

public interface IDataService
{
    List<MenuItem> LoadMenus();

    void SaveOrder(Order order);
}
