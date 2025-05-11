using Microsoft.Data.SqlClient;
using Tutorial9.Model;

namespace Tutorial9.Controllers;
using System.Data;
using System.Data.SqlClient;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/warehouse")]
public sealed class WarehouseController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly string _connString;

    public WarehouseController(IConfiguration config)
    {
        _config = config;
        _connString = _config.GetConnectionString("Default")
                      ?? throw new InvalidOperationException("Brak connection stringa");
    }

    [HttpPost]
    public async Task<IActionResult> AddProductToWarehouse(
        [FromBody] ProductWarehouseRequest req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid || req.Amount <= 0)
            return BadRequest("Nieprawidłowe dane wejściowe");

        Console.WriteLine(req.IdProduct);
        Console.WriteLine(req.IdWarehouse);
        Console.WriteLine(req.Amount);
        Console.WriteLine(req.CreatedAt);
        
        await using var con = new SqlConnection(_connString);
        await con.OpenAsync(ct);

        await using var tran = con.BeginTransaction();   

        try
        {
            var parameters = new SqlParameter[1];
            parameters[0] = new SqlParameter("@id", req.IdProduct);
            
            bool productExists = await ExistsAsync(
                con, tran,
                "SELECT 1 FROM Product WHERE IdProduct=@id",
                parameters);

            if (!productExists)
                return NotFound($"Produkt {req.IdProduct} nie istnieje");

            var parameters2= new SqlParameter[1];
            parameters2[0] = new SqlParameter("@id",  req.IdWarehouse);
            bool warehouseExists = await ExistsAsync(
                con, tran,
                "SELECT 1 FROM Warehouse WHERE IdWarehouse=@id",
               parameters2);

            if (!warehouseExists)
                return NotFound($"Magazyn {req.IdWarehouse} nie istnieje");

            Object idOrder = await ScalarAsync<Object>(
                con, tran,
                @"SELECT TOP 1 IdOrder
                    FROM [Order]
                   WHERE IdProduct = @prod
                     AND Amount    = @amt
                     AND CreatedAt <  @created",
                new("@prod", req.IdProduct),
                new("@amt",  req.Amount),
                new("@created", req.CreatedAt));

            if (idOrder is null)
                return NotFound("Brak pasującego zamówienia");

            var parameters3 = new SqlParameter[1];
            parameters3[0] = new SqlParameter("@id", idOrder);
            bool alreadyDone = await ExistsAsync(
                con, tran,
                "SELECT 1 FROM Product_Warehouse WHERE IdOrder = @id",
              parameters3);

            if (alreadyDone)
                return Conflict("Zamówienie zostało już zrealizowane");

            var parameters4 = new SqlParameter[1];
            parameters4[0] = new SqlParameter("@id", idOrder);
            await ExecAsync(
                con, tran,
                @"UPDATE [Order] SET FulfilledAt = GETDATE()
                  WHERE IdOrder = @id",
                parameters4);

            var parameters5 = new SqlParameter[1];
            parameters5[0] = new SqlParameter("@prod", req.IdProduct);
            
            decimal unitPrice = await ScalarAsync<decimal>(
                con, tran,
                "SELECT Price FROM Product WHERE IdProduct=@prod",
               parameters5);

            decimal totalPrice = unitPrice * req.Amount;

            int newId = await ScalarAsync<int>(
                con, tran,
                @"INSERT INTO Product_Warehouse
                    (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt)
                  VALUES (@wh, @prod, @ord, @amt, @price, GETDATE());
                  SELECT SCOPE_IDENTITY();",
                new("@wh", req.IdWarehouse),
                new("@prod", req.IdProduct),
                new("@ord", idOrder),
                new("@amt", req.Amount),
                new("@price", totalPrice));

            await tran.CommitAsync(ct);
            return CreatedAtAction(nameof(AddProductToWarehouse),
                     new ProductWarehouseResponse(newId));
        }
        catch (SqlException ex)
        {
            Console.WriteLine(ex.Message);
            await tran.RollbackAsync(ct);
            return StatusCode(500, "Błąd bazy danych");
        }
    }

    //  Zadanie 2
    [HttpPost("procedure")]
    public async Task<IActionResult> AddViaProcedure(
        [FromBody] ProductWarehouseRequest req,
        CancellationToken ct)
    {
        if (!ModelState.IsValid || req.Amount <= 0)
            return BadRequest("Nieprawidłowe dane wejściowe");

        await using var con = new SqlConnection(_connString);
        await using var cmd = new SqlCommand("AddProductToWarehouse", con)
        {
            CommandType = CommandType.StoredProcedure
        };

        cmd.Parameters.AddWithValue("@IdProduct",  req.IdProduct);
        cmd.Parameters.AddWithValue("@IdWarehouse", req.IdWarehouse);
        cmd.Parameters.AddWithValue("@Amount",     req.Amount);
        cmd.Parameters.AddWithValue("@CreatedAt",  req.CreatedAt);

        await con.OpenAsync(ct);

        try
        {
            var result = await cmd.ExecuteScalarAsync(ct); 
            Console.WriteLine(result);
          if (result is int id)
                return CreatedAtAction(nameof(AddViaProcedure),
                         new ProductWarehouseResponse(id));
          if (result is Decimal id2)
              return CreatedAtAction(nameof(AddViaProcedure),
                  new ProductWarehouseResponse((int)id2));
          
            return StatusCode(500, "Procedura nie zwróciła Id");
        }
        catch (SqlException ex)
        {
            return StatusCode((ex.Number >= 50000) ? 400 : 500, ex.Message);
        }
    }

    private static async Task<bool> ExistsAsync(
        SqlConnection con, SqlTransaction tran,
        string sql, params SqlParameter[] pars)
    {
        await using var cmd = new SqlCommand(sql, con, tran);
        cmd.Parameters.AddRange(pars);
        var res = await cmd.ExecuteScalarAsync();
        return res is not null;
    }

    private static async Task<T> ScalarAsync<T>(
        SqlConnection con, SqlTransaction tran,
        string sql, params SqlParameter[] pars)
    {
        await using var cmd = new SqlCommand(sql, con, tran);
        cmd.Parameters.AddRange(pars);
        object? val = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(val!, typeof(T));
    }

    private static async Task ExecAsync(
        SqlConnection con, SqlTransaction tran,
        string sql, params SqlParameter[] pars)
    {
        await using var cmd = new SqlCommand(sql, con, tran);
        cmd.Parameters.AddRange(pars);
        await cmd.ExecuteNonQueryAsync();
    }
}
