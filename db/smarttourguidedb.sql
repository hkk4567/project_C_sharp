-- phpMyAdmin SQL Dump
-- version 5.2.1
-- https://www.phpmyadmin.net/
--
-- Host: 127.0.0.1
-- Generation Time: Mar 06, 2026 at 04:11 AM
-- Server version: 10.4.32-MariaDB
-- PHP Version: 8.2.12

SET SQL_MODE = "NO_AUTO_VALUE_ON_ZERO";
START TRANSACTION;
SET time_zone = "+00:00";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8mb4 */;

--
-- Database: `smarttourguidedb`
--

-- --------------------------------------------------------

--
-- Table structure for table `geofencesettings`
--

CREATE TABLE `geofencesettings` (
  `Id` int(11) NOT NULL,
  `PoiId` int(11) NOT NULL,
  `TriggerRadiusInMeters` double NOT NULL,
  `Priority` int(11) NOT NULL,
  `CooldownInSeconds` int(11) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `geofencesettings`
--

INSERT INTO `geofencesettings` (`Id`, `PoiId`, `TriggerRadiusInMeters`, `Priority`, `CooldownInSeconds`) VALUES
(1, 1, 50, 1, 300),
(3, 3, 50, 1, 300),
(4, 4, 50, 1, 300),
(5, 5, 50, 1, 300);

-- --------------------------------------------------------

--
-- Table structure for table `mediaassets`
--

CREATE TABLE `mediaassets` (
  `Id` int(11) NOT NULL,
  `PoiId` int(11) NOT NULL,
  `Type` int(11) NOT NULL,
  `UrlOrContent` longtext NOT NULL,
  `LanguageCode` longtext NOT NULL,
  `VoiceGender` longtext DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `mediaassets`
--

INSERT INTO `mediaassets` (`Id`, `PoiId`, `Type`, `UrlOrContent`, `LanguageCode`, `VoiceGender`) VALUES
(1, 1, 1, 'https://example.com/audio/buncha-huonglien-vi.mp3', 'vi-VN', 'Female'),
(2, 3, 0, '/uploads/images/f0d07225-1d27-430b-95fb-f95b6ff9b754.jpg', 'vi-VN', NULL),
(3, 4, 0, '/uploads/images/b9571fe0-314e-471e-b1cb-05760cbf22bb.jpg', 'vi-VN', NULL),
(4, 1, 0, '/uploads/images/1ee4e31f-b378-48db-ab3a-4917ea2c4ead.jpg', 'vi-VN', NULL),
(5, 5, 0, '/uploads/images/639b3fe9-1901-4911-be53-beccecaec017.jpg', 'vi-VN', NULL),
(6, 5, 0, '/uploads/images/0b6188bd-b40d-41e3-850b-60c50e0db0bc.jpg', 'vi-VN', NULL),
(7, 5, 0, '/uploads/images/4229ae68-c96f-4691-9af9-f1020149ca87.jpg', 'vi-VN', NULL),
(8, 5, 1, '/uploads/audio/9e8df702-87f7-4818-9d52-3564d781bd21.mp3', 'vi-VN', NULL),
(15, 1, 1, '/uploads/audio/7a8c1ef9-787b-46ed-8102-a2482e8ebbb2.mp3', 'en-US', NULL);

-- --------------------------------------------------------

--
-- Table structure for table `pois`
--

CREATE TABLE `pois` (
  `Id` int(11) NOT NULL,
  `Name` varchar(200) NOT NULL,
  `Description` longtext NOT NULL,
  `Latitude` double NOT NULL,
  `Longitude` double NOT NULL,
  `Address` longtext NOT NULL,
  `Status` int(11) NOT NULL,
  `OwnerId` int(11) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `pois`
--

INSERT INTO `pois` (`Id`, `Name`, `Description`, `Latitude`, `Longitude`, `Address`, `Status`, `OwnerId`) VALUES
(1, 'Bún chả Hương Liên', 'Quán bún chả nổi tiếng Hà Nội, từng đón tiếp Tổng thống Obama. Đặc sản bún chả truyền thống.', 21.016492, 105.834132, '24 Lê Văn Hưu, Hai Bà Trưng, Hà Nội', 1, 2),
(3, 'Hoang’s Kitchen - Vietnamese cuisine & Vegan food', 'Phục vụ món Việt & chay ngon', 10.774218, 106.696606, '45 Thủ Khoa Huân, Q1, TP. Hồ Chí Minh', 1, 2),
(4, 'Tung\'s Restaurant - Vietnamese Cuisine & vegetarian Food', 'Quán truyền thống, nhiều món Việt đặc sắc, đông khách.', 10.772648, 106.696484, '230 Lê Thánh Tôn, Phường Sài Gòn, Quận 1, Thành phố Hồ Chí Minh 700000, Vietnam', 0, 2),
(5, 'HOME Saigon - HOME Vietnamese Restaurant', 'Nhà hàng món Việt ngon và lịch sự.', 10.783294, 106.6897, '216/4 Điện Biên Phủ, Phường Võ Thị Sáu, Quận 3, Thành phố Hồ Chí Minh 700000, Vietnam', 1, 2);

-- --------------------------------------------------------

--
-- Table structure for table `poitranslations`
--

CREATE TABLE `poitranslations` (
  `Id` int(11) NOT NULL,
  `PoiId` int(11) NOT NULL,
  `LanguageCode` varchar(10) NOT NULL,
  `TranslatedName` varchar(200) NOT NULL,
  `TranslatedDescription` longtext NOT NULL,
  `TranslatedAddress` longtext NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `poitranslations`
--

INSERT INTO `poitranslations` (`Id`, `PoiId`, `LanguageCode`, `TranslatedName`, `TranslatedDescription`, `TranslatedAddress`) VALUES
(1, 1, 'en-US', 'huong lien res', 'hehe', '24 P. Lê Văn Hưu, Phan Chu Trinh');

-- --------------------------------------------------------

--
-- Table structure for table `tourdetails`
--

CREATE TABLE `tourdetails` (
  `Id` int(11) NOT NULL,
  `TourId` int(11) NOT NULL,
  `PoiId` int(11) NOT NULL,
  `OrderIndex` int(11) NOT NULL,
  `Note` longtext DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `tourdetails`
--

INSERT INTO `tourdetails` (`Id`, `TourId`, `PoiId`, `OrderIndex`, `Note`) VALUES
(4, 2, 1, 1, NULL),
(5, 2, 3, 2, NULL),
(6, 2, 5, 3, NULL);

-- --------------------------------------------------------

--
-- Table structure for table `tours`
--

CREATE TABLE `tours` (
  `Id` int(11) NOT NULL,
  `Name` varchar(200) NOT NULL,
  `Description` longtext NOT NULL,
  `ThumbnailUrl` longtext DEFAULT NULL,
  `EstimatedDurationMinutes` int(11) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `tours`
--

INSERT INTO `tours` (`Id`, `Name`, `Description`, `ThumbnailUrl`, `EstimatedDurationMinutes`) VALUES
(2, 'quán ngon', 'quán ăn ngon', '/uploads/images/1ee4e31f-b378-48db-ab3a-4917ea2c4ead.jpg', 0);

-- --------------------------------------------------------

--
-- Table structure for table `userlocationlogs`
--

CREATE TABLE `userlocationlogs` (
  `Id` bigint(20) NOT NULL,
  `DeviceId` longtext DEFAULT NULL,
  `Latitude` double NOT NULL,
  `Longitude` double NOT NULL,
  `Timestamp` datetime(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

-- --------------------------------------------------------

--
-- Table structure for table `users`
--

CREATE TABLE `users` (
  `Id` int(11) NOT NULL,
  `Username` varchar(100) NOT NULL,
  `PasswordHash` longtext NOT NULL,
  `FullName` varchar(100) NOT NULL,
  `Email` longtext NOT NULL,
  `Role` int(11) NOT NULL,
  `IsLocked` tinyint(1) NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `users`
--

INSERT INTO `users` (`Id`, `Username`, `PasswordHash`, `FullName`, `Email`, `Role`, `IsLocked`) VALUES
(1, 'tourist01', '$2a$11$RJtrQV5Jd7pwNPsqz4u19u7/h/xNYI7oAWk10cIVneketdsCpRt36', 'Nguyễn Văn Du Lịch', 'dulich123@gmail.com', 0, 0),
(2, 'boothowner01', '$2a$11$RJtrQV5Jd7pwNPsqz4u19u7/h/xNYI7oAWk10cIVneketdsCpRt36', 'Trần Thị Chủ Quán', 'boothowner01@gmail.com', 1, 0),
(3, 'admin01', '$2a$11$RJtrQV5Jd7pwNPsqz4u19u7/h/xNYI7oAWk10cIVneketdsCpRt36', 'Admin Hệ Thống', 'admin01@gmail.com', 2, 0),
(4, 'DL1', '$2a$11$ukikifuDqYDtfepjVsicFu6b7fPs7JUaxDZH05mZK/cew6IzfRpG2', 'du lich 123', 'dulich2@gmail.com', 0, 0);

-- --------------------------------------------------------

--
-- Table structure for table `__efmigrationshistory`
--

CREATE TABLE `__efmigrationshistory` (
  `MigrationId` varchar(150) NOT NULL,
  `ProductVersion` varchar(32) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;

--
-- Dumping data for table `__efmigrationshistory`
--

INSERT INTO `__efmigrationshistory` (`MigrationId`, `ProductVersion`) VALUES
('20260129141650_InitialCreate', '8.0.2'),
('20260129153305_InitialDb', '8.0.2'),
('20260131035705_AddUserLocationLog', '8.0.2'),
('20260303100335_AddIsLockedToUser', '8.0.2'),
('20260304123005_AddTourAndDetails', '8.0.2'),
('20260304124207_RemoveOwnerFromTour', '8.0.2'),
('20260305062815_AddPoiTranslation', '8.0.2'),
('20260305111808_RemoveAudioFromTranslation', '8.0.2');

--
-- Indexes for dumped tables
--

--
-- Indexes for table `geofencesettings`
--
ALTER TABLE `geofencesettings`
  ADD PRIMARY KEY (`Id`),
  ADD UNIQUE KEY `IX_GeofenceSettings_PoiId` (`PoiId`);

--
-- Indexes for table `mediaassets`
--
ALTER TABLE `mediaassets`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `IX_MediaAssets_PoiId` (`PoiId`);

--
-- Indexes for table `pois`
--
ALTER TABLE `pois`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `IX_Pois_OwnerId` (`OwnerId`);

--
-- Indexes for table `poitranslations`
--
ALTER TABLE `poitranslations`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `IX_PoiTranslations_PoiId` (`PoiId`);

--
-- Indexes for table `tourdetails`
--
ALTER TABLE `tourdetails`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `IX_TourDetails_PoiId` (`PoiId`),
  ADD KEY `IX_TourDetails_TourId` (`TourId`);

--
-- Indexes for table `tours`
--
ALTER TABLE `tours`
  ADD PRIMARY KEY (`Id`);

--
-- Indexes for table `userlocationlogs`
--
ALTER TABLE `userlocationlogs`
  ADD PRIMARY KEY (`Id`),
  ADD KEY `IX_UserLocationLogs_Timestamp` (`Timestamp`);

--
-- Indexes for table `users`
--
ALTER TABLE `users`
  ADD PRIMARY KEY (`Id`),
  ADD UNIQUE KEY `IX_Users_Username` (`Username`);

--
-- Indexes for table `__efmigrationshistory`
--
ALTER TABLE `__efmigrationshistory`
  ADD PRIMARY KEY (`MigrationId`);

--
-- AUTO_INCREMENT for dumped tables
--

--
-- AUTO_INCREMENT for table `geofencesettings`
--
ALTER TABLE `geofencesettings`
  MODIFY `Id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=6;

--
-- AUTO_INCREMENT for table `mediaassets`
--
ALTER TABLE `mediaassets`
  MODIFY `Id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=16;

--
-- AUTO_INCREMENT for table `pois`
--
ALTER TABLE `pois`
  MODIFY `Id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=6;

--
-- AUTO_INCREMENT for table `poitranslations`
--
ALTER TABLE `poitranslations`
  MODIFY `Id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=2;

--
-- AUTO_INCREMENT for table `tourdetails`
--
ALTER TABLE `tourdetails`
  MODIFY `Id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=7;

--
-- AUTO_INCREMENT for table `tours`
--
ALTER TABLE `tours`
  MODIFY `Id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=3;

--
-- AUTO_INCREMENT for table `userlocationlogs`
--
ALTER TABLE `userlocationlogs`
  MODIFY `Id` bigint(20) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT for table `users`
--
ALTER TABLE `users`
  MODIFY `Id` int(11) NOT NULL AUTO_INCREMENT, AUTO_INCREMENT=5;

--
-- Constraints for dumped tables
--

--
-- Constraints for table `geofencesettings`
--
ALTER TABLE `geofencesettings`
  ADD CONSTRAINT `FK_GeofenceSettings_Pois_PoiId` FOREIGN KEY (`PoiId`) REFERENCES `pois` (`Id`) ON DELETE CASCADE;

--
-- Constraints for table `mediaassets`
--
ALTER TABLE `mediaassets`
  ADD CONSTRAINT `FK_MediaAssets_Pois_PoiId` FOREIGN KEY (`PoiId`) REFERENCES `pois` (`Id`) ON DELETE CASCADE;

--
-- Constraints for table `pois`
--
ALTER TABLE `pois`
  ADD CONSTRAINT `FK_Pois_Users_OwnerId` FOREIGN KEY (`OwnerId`) REFERENCES `users` (`Id`);

--
-- Constraints for table `poitranslations`
--
ALTER TABLE `poitranslations`
  ADD CONSTRAINT `FK_PoiTranslations_Pois_PoiId` FOREIGN KEY (`PoiId`) REFERENCES `pois` (`Id`) ON DELETE CASCADE;

--
-- Constraints for table `tourdetails`
--
ALTER TABLE `tourdetails`
  ADD CONSTRAINT `FK_TourDetails_Pois_PoiId` FOREIGN KEY (`PoiId`) REFERENCES `pois` (`Id`),
  ADD CONSTRAINT `FK_TourDetails_Tours_TourId` FOREIGN KEY (`TourId`) REFERENCES `tours` (`Id`) ON DELETE CASCADE;

--
-- Constraints for table `userlocationlogs`
--
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
