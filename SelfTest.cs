using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Island.Providers;
namespace Island;
internal static class SelfTest
{
    public static int Run()
    {
        try {
            void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);}
            Check(LinkClean.Try("https://shop.example/a?utm_source=x&id=7&fbclid=abc", out var clean) && clean == "https://shop.example/a?id=7", "tracking params stripped");
            Check(!LinkClean.Try("https://shop.example/a and more", out _), "clean link rejects surrounding text");
            Check(Money.Convert(12345,"usd")==123.45m,"USD minor units");Check(Money.Convert(1240,"JPY")==1240m,"JPY zero decimals");Check(Money.Convert(12345,"kwd")==12.345m,"KWD three decimals");Check(Money.Convert(100,"isk")==1m,"Stripe ISK charge units");
            var today=new DateTimeOffset(DateTime.Today.AddHours(12));var summary=new RevenueSummary(new[]{new Sale("1","Stripe","USD",10,"",today),new Sale("2","Polar","EUR",20,"",today),new Sale("3","Polar","USD",5,"",today),new Sale("4","Stripe","USD",100,"",new DateTimeOffset(DateTime.Today.AddDays(-1)))});
            Check(summary.Today.Contains("USD 15")&&summary.Today.Contains("EUR 20")&&!summary.Today.Contains("115"),"currency isolation and local date");
            byte[] plain=System.Text.Encoding.UTF8.GetBytes("Island DPAPI test");byte[] encrypted=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);Check(!encrypted.SequenceEqual(plain),"encrypted bytes");Check(ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser).SequenceEqual(plain),"DPAPI roundtrip");
            File.WriteAllText("self-test-result.txt","PASS: currency conversions, currency isolation, local today boundary, DPAPI roundtrip.");return 0;
        }catch(Exception ex){File.WriteAllText("self-test-result.txt","FAIL: "+ex.Message);return 1;}
    }
}
