-- ABC Retailers Authentication Database Script
-- NOTE: This script is OPTIONAL - the application will automatically create tables on startup
-- Use this only if you want to manually create the tables in SQL Server Management Studio

-- Create Users table
CREATE TABLE Users (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Username NVARCHAR(100) NOT NULL,
    PasswordHash NVARCHAR(256) NOT NULL,
    Role NVARCHAR(20) NOT NULL -- 'Customer' or 'Admin'
);

-- Insert sample users
INSERT INTO Users (Username, PasswordHash, Role)
VALUES
('customer1', 'password123', 'Customer'),
('admin1', 'adminpass456', 'Admin');

-- Display all users
SELECT * FROM Users;

-- Create Cart table
CREATE TABLE Cart (
    Id INT PRIMARY KEY IDENTITY,
    CustomerUsername NVARCHAR(100),
    ProductId NVARCHAR(100),
    Quantity INT
);

-- Display Cart table
SELECT * FROM Cart;
