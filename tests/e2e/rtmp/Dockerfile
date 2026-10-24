# nginx with nginx-http-flv-module, the versions production builds, and production's configuration template.
FROM alpine:3.20 AS build
ARG NGINX_VERSION=1.26.3
ARG FLV_VERSION=1.2.12
RUN apk add --no-cache build-base pcre-dev zlib-dev openssl-dev wget
WORKDIR /src
RUN wget -q https://nginx.org/download/nginx-${NGINX_VERSION}.tar.gz && tar xzf nginx-${NGINX_VERSION}.tar.gz \
 && wget -q https://github.com/winshining/nginx-http-flv-module/archive/refs/tags/v${FLV_VERSION}.tar.gz && tar xzf v${FLV_VERSION}.tar.gz
WORKDIR /src/nginx-${NGINX_VERSION}
RUN ./configure --prefix=/usr/local/nginx --add-module=/src/nginx-http-flv-module-${FLV_VERSION} \
      --conf-path=/etc/nginx/nginx.conf --with-threads --with-http_ssl_module --with-cc-opt="-Wimplicit-fallthrough=0" \
 && make -j4 && make install

FROM alpine:3.20
RUN apk add --no-cache pcre zlib openssl gettext
COPY --from=build /usr/local/nginx /usr/local/nginx
COPY --from=build /etc/nginx /etc/nginx
COPY nginx.conf.template /etc/nginx/nginx.conf.template
EXPOSE 1935 8088
CMD ["/bin/sh", "-c", "envsubst '$ON_PUBLISH $ON_PUBLISH_DONE $ON_PLAY $ON_PLAY_DONE' < /etc/nginx/nginx.conf.template > /etc/nginx/nginx.conf && exec /usr/local/nginx/sbin/nginx -g 'daemon off;'"]
