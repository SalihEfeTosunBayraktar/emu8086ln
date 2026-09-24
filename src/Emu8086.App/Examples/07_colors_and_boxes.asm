; Text mode colours and box-drawing characters written straight into video memory (B800:0000).
; Each screen cell is 2 bytes: character, attribute (background << 4 | foreground).
#make_COM#
org 100h

    mov ax, 0B800h
    mov es, ax

    ; 16 colour bars on row 2
    mov di, (2 * 80 + 4) * 2
    mov cx, 16
    mov ah, 0
bars:
    mov al, 0DBh            ; full block character
    mov es:[di], ax
    mov es:[di+2], ax
    add di, 6
    inc ah
    loop bars

    ; a double-line box frame at row 5 and row 9, column 10
    mov bl, 1Eh             ; yellow on blue
    mov dh, 5
    mov dl, 10
    call top_edge
    mov dh, 9
    call bottom_edge
    ret

; draws a horizontal box edge: DH = row, DL = column, BL = attribute
top_edge:
    mov al, 0C9h            ; top-left corner
    mov bh, 0BBh            ; top-right corner
    jmp edge
bottom_edge:
    mov al, 0C8h            ; bottom-left corner
    mov bh, 0BCh            ; bottom-right corner
edge:
    push ax
    mov al, 160             ; 80 columns * 2 bytes per row
    mul dh
    mov di, ax
    mov al, dl
    mov ah, 0
    shl ax, 1
    add di, ax
    pop ax
    mov ah, bl
    stosw                   ; ES:DI <- AX, DI += 2
    mov cx, 28
    mov al, 0CDh            ; horizontal double line
    rep stosw
    mov al, bh
    stosw
    ret
