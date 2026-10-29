using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kudubrowse.Core;

namespace Kudubrowse.Tests
{
    public static class RoomsTests
    {
        public static void Run(Action<string, Action> test)
        {
            test("Rooms: Personal and Work to start, add, rename, remove, kept", RoomsWork);
            test("Rooms: each tab's room is saved with it; unknown rooms fall back to the first", TabsKeepRooms);
            test("One running copy: a second start hands its address to the first", OneCopy);
            test("Downloads: files that can run programs are spotted; sizes in plain words", Downloads);
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{what}: expected \"{expected}\", got \"{actual}\"");
        }

        private static string Temp() => Path.Combine(Path.GetTempPath(), "kudubrowse-test-" + Guid.NewGuid().ToString("N") + ".txt");

        private static void RoomsWork()
        {
            var file = Temp();
            try
            {
                var r = Rooms.Load(file);
                Equal("Personal|Work", string.Join("|", r.All.Select(x => x.Name)), "the two to start");
                var shop = r.Add("Shopping");
                Equal("room-2", shop.Id, "next free id");
                r.Rename(shop, " Shops ");
                Equal(false, r.Remove(r.Default), "the first room stays");
                var again = Rooms.Load(file);
                Equal("Personal|Work|Shops", string.Join("|", again.All.Select(x => x.Name)), "kept");
                again.Remove(again.All[1]);
                Equal("Personal|Shops", string.Join("|", Rooms.Load(file).All.Select(x => x.Name)), "Work removed, not added back");
                Equal(Rooms.DefaultId, again.Find("room-99").Id, "unknown room: the first");
                Equal(false, Rooms.ValidId("../x"), "odd names refused");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void TabsKeepRooms()
        {
            var file = Temp();
            try
            {
                Session.Save(new[]
                {
                    new SavedTab { Url = "https://a.example/", Title = "A" },
                    new SavedTab { Url = "https://b.example/", Title = "B", Room = "room-1" }
                }, 0, file);
                var (tabs, _) = Session.Load(file);
                Equal("Default|room-1", string.Join("|", tabs.Select(t => t.Room)), "rooms kept");
                File.AppendAllText(file, "https://c.example/\tC\t..\\evil\n");
                Equal(Rooms.DefaultId, Session.Load(file).Tabs.Last().Room, "a strange room name isn't used");
            }
            finally
            {
                File.Delete(file);
            }
        }

        private static void OneCopy()
        {
            var name = "kudubrowse-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using (var first = new SingleInstance(name))
            using (var second = new SingleInstance(name))
            {
                Equal(true, first.IsFirst, "the first copy");
                Equal(false, second.IsFirst, "the second sees the first");
                string[] got = null;
                var done = new System.Threading.ManualResetEventSlim();
                first.Listen(a =>
                {
                    got = a;
                    done.Set();
                });
                System.Threading.Thread.Sleep(200);
                Equal(true, SingleInstance.Send(name, new[] { "https://example.com/a b", "--private" }), "sent");
                done.Wait(5000);
                Equal("https://example.com/a b|--private", got == null ? "(nothing)" : string.Join("|", got), "received");
            }
            Equal(false, SingleInstance.Send("kudubrowse-nobody-" + Guid.NewGuid().ToString("N"), new[] { "x" }, 300), "nothing running: not sent");
        }

        private static void Downloads()
        {
            Equal(true, DownloadRules.IsRisky("setup.EXE"), "programs");
            Equal(true, DownloadRules.IsRisky("invoice.pdf.js"), "a disguised script");
            Equal(false, DownloadRules.IsRisky("photo.jpg"), "pictures");
            Equal(false, DownloadRules.IsRisky("report.pdf"), "documents");
            Equal("830 KB", DownloadRules.Size(850000), "KB");
            Equal("4.8 MB", DownloadRules.Size(5000000), "MB");
            Equal("512 bytes", DownloadRules.Size(512), "bytes");
        }
    }
}
