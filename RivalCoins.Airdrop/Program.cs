using Microsoft.AspNetCore.Mvc;
using stellar_dotnet_sdk;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks.Dataflow;

const string AirdropAccountSeed = "";
const RivalCoins.Sdk.Network Network = RivalCoins.Sdk.Network.Mainnet;

const string MyAllowSpecificOrigins = "_myAllowSpecificOrigins";
const int MaxOperationsPerTransaction = 100;
const int StellarLedgerTimeSeconds = 5;

var builder = WebApplication.CreateBuilder(args);
var wallet = RivalCoins.Sdk.Wallet.Default[Network] with { AccountSecretSeed = AirdropAccountSeed };
await wallet.InitializeAsync();

Console.WriteLine($"wallet hash: {wallet.GetHashCode()}");

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = builder.Environment.ApplicationName,
        Version = "v1"
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      builder =>
                      {
                          builder.WithOrigins("https://test.rivalcoins.io", "https://rivalcoins.money");
                      });
});

var app = builder.Build();

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json",
                                    $"{builder.Environment.ApplicationName} v1"));
}

app.MapGet("/swag", () => "Hello Swagger!");

IPropagatorBlock<TIn, IList<TIn>> CreateBuffer<TIn>(TimeSpan timeSpan, int count)
{
    var inBlock = new BufferBlock<TIn>();
    var outBlock = new BufferBlock<IList<TIn>>();

    var outObserver = outBlock.AsObserver();
    inBlock.AsObservable()
            .Buffer(timeSpan, count)
            .ObserveOn(TaskPoolScheduler.Default)
            .Subscribe(outObserver);

    return DataflowBlock.Encapsulate(inBlock, outBlock);
}

var transactionNum = 1;
var airdropNum = 1;

async Task AirdropAsync(IList<(string Destination, stellar_dotnet_sdk.AssetTypeCreditAlphaNum Asset)> airdropInfos)
{
    const string MoneyAirdropAmount = "3.2876712";

    Console.WriteLine($"tx #{transactionNum++}");

    if (airdropInfos.Any())
    {
        using var server = new Server(RivalCoins.Sdk.Wallet.GetHorizonUri(wallet.Network));
        var airdropAccount = await server.Accounts.Account(wallet.Account.AccountId);
        var tx = new TransactionBuilder(airdropAccount);
        
        foreach (var airdropInfo in airdropInfos)
        {
            Console.WriteLine($"airdrop #{airdropNum} - {airdropInfo.Destination} to receive {airdropInfo.Asset.CanonicalName()}");

            tx.AddOperation(
                new PaymentOperation.Builder(
                    KeyPair.FromAccountId(airdropInfo.Destination),
                    airdropInfo.Asset,
                    MoneyAirdropAmount).Build());
        }

        var finalTx = tx.Build();
        finalTx.Sign(wallet.KeyPairWithSecretSeed, wallet.NetworkInfo);

        var result = await server.SubmitTransaction(finalTx);
        Console.WriteLine($"************************************ airdrop #{airdropNum++} success: {result.IsSuccess()}");
    }
}

var airdropBuffer = CreateBuffer<(string Destination, AssetTypeCreditAlphaNum Asset)>(TimeSpan.FromSeconds(StellarLedgerTimeSeconds), MaxOperationsPerTransaction);
var airdropExecution = new ActionBlock<IList<(string Destination, AssetTypeCreditAlphaNum Asset)>>(AirdropAsync);

airdropBuffer.LinkTo(airdropExecution);

app.MapGet("/airdrop", async ([FromQuery(Name = "destination")] string destination, [FromQuery(Name = "asset")] string asset) =>
{
    var success = false;

    try
    {
        var account = await wallet.Server.Accounts.Account(destination);
        var assetCannonicalName = asset.Replace('-', ':');
        var acceptsAsset = account.Balances.Any(b => b.Asset != null && b.Asset.CanonicalName() == assetCannonicalName);

        if (acceptsAsset)
        {
            success = airdropBuffer.Post((destination, (AssetTypeCreditAlphaNum)(Asset.Create(assetCannonicalName))));
        }

    }
    catch (Exception) { }

    return success ? "success" : "failure";
})
.RequireCors(MyAllowSpecificOrigins);

app.Run();