FROM nginx:alpine AS runtime

# 复制nginx配置
COPY nginx.conf /etc/nginx/nginx.conf

# 复制预构建产物
COPY dist /usr/share/nginx/html

# 暴露端口
EXPOSE 80

# 启动nginx
CMD ["nginx", "-g", "daemon off;"]
