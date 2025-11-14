# CLDV6212POEPart3


1. Overview

This project forms Part 2 of the ABC Retailers Cloud Solution, focusing on the development and deployment of a cloud-based online store using Microsoft Azure.
The solution is designed to support key retail operations such as customer management, product hosting, order processing, media storage, and backend automation using Azure Functions.

The system follows a service-driven architecture that leverages multiple Azure PaaS and serverless offerings. The primary goal is to build a scalable, secure, and cost-efficient e-commerce backend suitable for modern retail environments.

2. Key Features
2.1 Web Application

Developed with ASP.NET Core MVC (.NET 9.0)

Hosted on Azure App Service (PaaS)

Supports customer browsing, product viewing, and order placement

Implements secure role-based access using built-in ASP.NET Core Authorization

2.2 Data Management

Azure SQL Database – Stores user credentials and authentication data

Azure Table Storage – Stores products, customers, and orders

Entity Framework Core 8 used as ORM

Azure.Data.Tables SDK for Table Storage operations

2.3 Media Storage

Azure Blob Storage used for:

Product images

Payment proof documents

Application logs and metadata

Includes multiple blob containers such as:
payment-proofs, product-images, $logs, azure-webjobs-hosts, azure-webjobs-secrets

2.4 Order Processing (Asynchronous)

Azure Queue Storage handles queued order operations

Automatically triggers Azure Functions for:

Stock updates

Order notifications

Poison queues handle failed messages (order-notifications-poison and stock-updates-poison)

2.5 Serverless Backend

Azure Functions (.NET 8.0) used to:

Validate orders

Process payment proofs

Send notifications

Update inventory

Automatically scales based on demand (Consumption Plan)

3. Architecture Diagram (Summary)

Frontend: ASP.NET Core MVC
Backend: Azure Functions
Storage: SQL Database, Table Storage, Blob Storage
Messaging: Azure Queue Storage
Compute: Azure App Service

All resources are cloud-based and designed using a PaaS-centric architecture for improved scalability, reduced maintenance, and cost efficiency.

4. Technology Stack
Component	Technology
Web Framework	ASP.NET Core MVC (.NET 9.0)
Backend Microservices	Azure Functions (.NET 8.0)
Authentication Database	Azure SQL Database
Product & Order Data	Azure Table Storage
Media Storage	Azure Blob Storage
Messaging Queue	Azure Queue Storage
File Storage	Azure File Share
ORM	Entity Framework Core 8
SDKs	Azure.Data.Tables, HttpClient
5. Deployment Details
5.1 Azure Resources Deployed

Azure App Service

Azure SQL Database

Azure Storage Account

Blob Containers

Tables

Queues

File Share

Azure Functions (with function triggers)

5.2 Deployment Steps

Publish MVC web app to Azure App Service

Deploy FunctionApp via Visual Studio and connect storage triggers

Configure connection strings in App Settings

Create Azure Table Storage tables: Products, Customers, Orders

Create required Blob containers

Create queue names: order-notifications, stock-updates

Upload sample product images and proof-of-payment files for testing

6. How to Run the Application Locally
Prerequisites

.NET 9.0 SDK

Visual Studio 2022

Azure Functions Core Tools

Azure Storage Emulator or Azurite

SQL Server / Azure SQL connection

Steps

Clone the repository

Open the solution in Visual Studio

Configure appsettings.json with your Azure connection strings

Run the MVC project

Start the FunctionApp locally using F5

Test order placement, blob uploads, and queue-triggered operations

7. Known Issues

Poison queues fill when a function fails more than 5 times

Table Storage requires careful partitioning for optimum performance

Some deployments may require regenerating access keys when switching devices

8. Reference List

(A brief version—your full reference list with screenshots remains separate)

Microsoft Azure. 2024. Azure Blob Storage Documentation. Microsoft Docs. Accessed 10–15 November 2025.

Microsoft Azure. 2024. Azure Queue Storage Overview. Microsoft Docs. Accessed 10–15 November 2025.

Microsoft Azure. 2024. Azure Functions Documentation. Microsoft Docs. Accessed 10–15 November 2025.

Microsoft Azure. 2024. App Service Overview. Microsoft Docs. Accessed 10–15 November 2025.

Microsoft Azure. 2024. SQL Database Features. Microsoft Docs. Accessed 10–15 November 2025.

9. Author

Orearabetse Riba
BSc Computer & Information Sciences (Application Development)
IIE MSA — 2025
