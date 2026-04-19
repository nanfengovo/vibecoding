# 构建阶段
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制项目文件并还原依赖
COPY QuantTrading.Api.csproj .
RUN dotnet restore

# 复制所有源代码并构建
COPY . .
RUN dotnet publish -c Release -o /app/publish

# 运行阶段
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ENV TZ=Asia/Shanghai

# 复制发布文件
COPY --from=build /app/publish .

# 设置环境变量
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Production

# 暴露端口
EXPOSE 5000

# 启动应用
ENTRYPOINT ["dotnet", "QuantTrading.Api.dll"]
