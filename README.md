# PetShop ASP.NET Core web application
<h3>ASP.NET Core 10 MVC web app using MSSQL, bootstrap, EF Core, identity and docker</h3>

<p align="center">
  <img width="600"  src="https://user-images.githubusercontent.com/110489710/209929509-3ed325ce-c8b4-40f4-a2f7-a2f7ad0ff727.png">
</p>

<h3>Docker instructions</h3>
pulling the database image to your local machine:

```javascript
  $ docker pull yarins2000/petshopdb:1.0
```

building the app image and running the containers:

```javascript
  $ docker-compose up -d --build
```
then, open a browser and write in the URL: localhost:3000

The app image is built from the local source by `docker-compose` (the published
`petshopapp:1.0` image on Docker Hub is the older .NET 6 build). The container listens on
port 8080 internally — the ASP.NET Core default since .NET 8 — and is published on 3000.
The connection string is supplied to the container through the
`ConnectionStrings__DefaultConnection` environment variable; the value in
`docker-compose.yml` is the demo credential for the bundled `petshopdb` image. Override it
to point at a different SQL Server.

<h3>About the web app</h3>
This is a pet shop web when you can view the animals, add comments and filter them by their category.
You can also register to the website and this provides you an access to add, edit and delete an animal.
If you are an admin, you can also edit the registrated users and add/edit/delete their roles(e.g admin).

for login as an admin, there is a default admin user:
email: admin@admin.com
password: Admin123
