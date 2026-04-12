// File này khai báo global using để dùng 
//chung trong toàn bộ project Mobile.

// Messaging nội bộ giữa các thành phần (MVVM Toolkit).
global using CommunityToolkit.Mvvm.Messaging;

// Bộ thư viện bản đồ Mapsui.
global using Mapsui;
global using Mapsui.Layers;
global using Mapsui.Nts;
global using Mapsui.Projections;
global using Mapsui.Providers;
global using Mapsui.Styles;
global using Mapsui.Tiling;
global using Mapsui.UI.Maui;

// Alias để tránh trùng tên với model/location khác trong dự án.
global using MauiLocation = Microsoft.Maui.Devices.Sensors;

// Thư viện hình học dùng cho xử lý vùng/geofence.
global using NetTopologySuite.Geometries;

// Audio và các namespace nội bộ của ứng dụng.
global using Plugin.Maui.Audio;
global using SmartTourGuide.Mobile.Models;
global using SmartTourGuide.Mobile.Services;

// Tiện ích hệ thống và lưu trữ cục bộ.
global using System.Globalization;
global using SQLite;

// Alias tài nguyên đa ngôn ngữ để gọi chuỗi ngắn gọn hơn.
global using AppRes = SmartTourGuide.Mobile.Resources.Strings.AppResources;